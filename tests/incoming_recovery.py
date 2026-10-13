"""Real process crash recovery; all data, inboxes and junction targets are temporary."""
import hashlib
import http.client
import json
import os
from pathlib import Path
import queue
import re
import socket
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
PREFIX = ".landrop-incoming-"
KIND = "LAN-Drop-Incoming-v1"


def wait_for(check, message, timeout=10):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(.05)
    raise AssertionError(message)


class Server:
    def __init__(self, data, inbox):
        data.mkdir(exist_ok=True)
        (data / "settings.json").write_text(json.dumps({"Path": str(inbox)}))
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            self.port = sock.getsockname()[1]
        self.base = f"http://127.0.0.1:{self.port}"
        lines = queue.Queue()
        self.process = subprocess.Popen(
            ["dotnet", str(ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"),
             f"--Port={self.port}", "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
             f"--DataDirectory={data}"], cwd=ROOT / "src/LanDrop",
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
            encoding="utf-8", errors="replace")
        threading.Thread(target=lambda: [lines.put(line) for line in self.process.stdout], daemon=True).start()
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            try:
                match = re.search(r"#admin=([a-f0-9]+)", lines.get(timeout=.5))
                if match:
                    self.admin = {"X-LanDrop-Admin": match[1]}
                    self.request("/api/admin")
                    return
            except queue.Empty:
                if self.process.poll() is not None:
                    break
        self.kill()
        raise AssertionError("Recovery test server did not start")

    def request(self, path, method="GET", body=None, headers=None, expected=200):
        headers = dict(self.admin if headers is None else headers)
        if method == "PUT":
            headers.setdefault("X-LanDrop-SHA256", hashlib.sha256(body or b"").hexdigest())
        req = urllib.request.Request(self.base + path, data=body, method=method, headers=headers)
        try:
            response = urllib.request.urlopen(req, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            assert response.status == expected, (path.split("?")[0], response.status)
            return json.loads(raw) if raw else None

    def json(self, path, value, headers=None):
        return self.request(path, "POST", json.dumps(value).encode(),
                            {**(self.admin if headers is None else headers), "Content-Type": "application/json"})

    def folder(self, inbox):
        return self.json("/api/admin/folder", {"path": str(inbox)})

    def phone(self):
        qr = self.request("/api/admin/qr?address=127.0.0.1")
        code = urllib.parse.parse_qs(urllib.parse.urlparse(qr["link"]).fragment)["pair"][0]
        phone = self.json("/api/pair", {"code": code, "name": "Recovery fixture"}, {})
        device = next(d["id"] for d in self.request("/api/admin")["devices"] if d["name"] == "Recovery fixture")
        self.request(f"/api/admin/devices/{device}/approve", "POST", b"")
        return {"X-LanDrop-Session": phone["token"]}

    def offer(self, name, size, phone):
        offer = self.json("/api/offers", {"name": name, "size": size}, phone)
        self.request(f"/api/admin/offers/{offer['id']}/approve", "POST", b"")
        return offer["id"]

    def upload(self, name, payload, phone, headers=None, expected=200):
        offer = self.offer(name, len(payload), phone)
        path = "/api/files?" + urllib.parse.urlencode({"name": name, "offer": offer})
        return self.request(path, "PUT", payload, {**phone, **(headers or {})}, expected)

    def partial(self, inbox, name, phone):
        payload = b"synthetic recovery fixture\n" * 65536
        prefix = payload[:262144]
        offer = self.offer(name, len(payload), phone)
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=10)
        conn.putrequest("PUT", "/api/files?" + urllib.parse.urlencode({"name": name, "offer": offer}))
        conn.putheader("Content-Length", str(len(payload)))
        conn.putheader("X-LanDrop-Session", phone["X-LanDrop-Session"])
        conn.putheader("X-LanDrop-SHA256", hashlib.sha256(payload).hexdigest())
        conn.endheaders()
        conn.send(prefix)
        try:
            part = wait_for(lambda: next((p for p in inbox.glob(PREFIX + "*/.upload-*.part")
                                          if p.stat().st_size > 0), None),
                            "Partial upload never wrote bytes")
            return conn, part, payload[len(prefix):], payload
        except BaseException:
            conn.close()
            raise

    def stop(self):
        if self.process.poll() is None:
            self.request("/api/admin/shutdown", "POST", b"")
            assert self.process.wait(timeout=20) == 0

    def kill(self):
        if self.process.poll() is None:
            self.process.kill()
            self.process.wait(timeout=10)


def owned_fixture(inbox, **marker_changes):
    run = uuid.uuid4().hex
    path = inbox / (PREFIX + run)
    path.mkdir()
    marker = {"Kind": KIND, "Run": run, **marker_changes}
    (path / ".landrop-owner").write_text(json.dumps(marker))
    (path / ".landrop-lease").write_bytes(b"")
    part = path / (".upload-" + uuid.uuid4().hex + ".part")
    part.write_bytes(b"synthetic incomplete fixture")
    return path


def main():
    with tempfile.TemporaryDirectory(prefix="landrop-incoming-recovery-") as directory:
        root = Path(directory)
        inbox = root / "inbox"; inbox.mkdir()
        alternate = root / "alternate"; alternate.mkdir()
        external = root / "external"; external.mkdir()
        sentinel = external / "keep.txt"; sentinel.write_bytes(b"external sentinel")
        servers, connections, junctions = [], [], []
        locked = None
        try:
            crashed = Server(root / "crashed-data", inbox); servers.append(crashed)
            phone = crashed.phone()
            final = crashed.upload("completed.bin", b"verified completed bytes", phone)
            conn, part, _, _ = crashed.partial(inbox, "interrupted.bin", phone); connections.append(conn)
            run = part.parent
            assert json.loads((run / ".landrop-owner").read_text()) == {"Kind": KIND, "Run": run.name[len(PREFIX):]}
            crashed.kill(); conn.close()
            assert part.exists() and part.stat().st_size > 0, "Force kill must leave actual incomplete bytes"
            restarted = Server(root / "crashed-data", inbox); servers.append(restarted)
            assert not run.exists(), "Restart must remove precisely the abandoned owned staging directory"
            assert not (inbox / "interrupted.bin").exists()
            assert (inbox / final["name"]).read_bytes() == b"verified completed bytes"
            assert restarted.request("/api/admin")["recoveredIncompleteUploads"] == 1
            restarted.request("/api/state", headers=phone, expected=401)

            # Another instance must respect the live instance's filesystem lease.
            phone = restarted.phone()
            conn, live_part, remainder, payload = restarted.partial(inbox, "live.bin", phone); connections.append(conn)
            cleaner = Server(root / "cleaner-data", inbox); servers.append(cleaner)
            assert live_part.exists(), "Startup must preserve a live transfer"
            conn.send(remainder)
            response = conn.getresponse(); raw = response.read()
            assert response.status == 200
            receipt = json.loads(raw)
            assert receipt["sha256"] == hashlib.sha256(payload).hexdigest()
            assert (inbox / receipt["name"]).read_bytes() == payload
            conn.close()
            cleaner.stop()

            # Switching folders only cleans the selected target; the admitted request
            # keeps its original destination and still completes successfully.
            untouched = owned_fixture(alternate)
            conn, old_part, remainder, payload = restarted.partial(inbox, "before-switch.bin", phone); connections.append(conn)
            third = root / "third"; third.mkdir()
            unrelated = owned_fixture(third)
            restarted.folder(alternate)
            assert not untouched.exists() and unrelated.exists()
            assert old_part.exists()
            conn.send(remainder)
            response = conn.getresponse(); result = json.loads(response.read())
            assert response.status == 200
            assert (inbox / result["name"]).read_bytes() == payload
            assert not (alternate / result["name"]).exists()
            conn.close()

            # Disconnect and checksum failure leave no staged bytes or final file.
            conn, canceled_part, _, _ = restarted.partial(alternate, "cancel.bin", phone); connections.append(conn)
            conn.close()
            wait_for(lambda: not canceled_part.exists(), "Canceled request left staged bytes")
            assert not (alternate / "cancel.bin").exists()
            error = restarted.upload("bad-digest.bin", b"wrong bytes", phone,
                                     {"X-LanDrop-SHA256": "0" * 64}, 422)
            assert error["code"] == "checksum_mismatch"
            assert not list(alternate.glob(PREFIX + "*/.upload-*.part"))
            assert not (alternate / "bad-digest.bin").exists()
            conn, shutdown_part, _, _ = restarted.partial(alternate, "shutdown.bin", phone); connections.append(conn)
            restarted.stop(); conn.close()
            assert not shutdown_part.exists(), "Graceful stop left incomplete bytes"
            assert unrelated.exists(), "Unselected directory must remain untouched"

            # Conservative recovery: foreign files, malformed markers, nested data,
            # lock contention and filesystem redirects are never recursively erased.
            foreign = inbox / "legacy.part"; foreign.write_bytes(b"legacy foreign data")
            unmarked = inbox / (PREFIX + uuid.uuid4().hex); unmarked.mkdir()
            (unmarked / "keep.part").write_bytes(b"unmarked data")
            malformed = owned_fixture(inbox)
            (malformed / ".landrop-owner").write_text("{broken")
            wrong_kind = owned_fixture(inbox, Kind="Other-App")
            traversal = owned_fixture(inbox, Run="../../external")
            extra_marker = owned_fixture(inbox, Other="unexpected field")
            duplicate_marker = owned_fixture(inbox)
            duplicate_run = duplicate_marker.name[len(PREFIX):]
            (duplicate_marker / ".landrop-owner").write_text(
                '{"Kind":"' + KIND + '","Run":"' + duplicate_run + '","Run":"' + duplicate_run + '"}')
            wrong_lease = owned_fixture(inbox); (wrong_lease / ".landrop-lease").write_bytes(b"not a lease")
            invalid_prefix = inbox / (PREFIX + "not-a-run"); invalid_prefix.mkdir()
            (invalid_prefix / "keep.part").write_bytes(b"invalid directory identity")
            nested = owned_fixture(inbox); (nested / "nested").mkdir()
            (nested / "nested" / "keep.txt").write_bytes(b"nested foreign data")
            extra = owned_fixture(inbox); (extra / "keep.txt").write_bytes(b"unexpected foreign file")
            wrong_suffix = owned_fixture(inbox)
            (wrong_suffix / (".upload-" + uuid.uuid4().hex + ".part.extra")).write_bytes(b"invalid payload identity")
            preserved = [unmarked, malformed, wrong_kind, traversal, extra_marker, duplicate_marker, wrong_suffix,
                         wrong_lease, invalid_prefix, nested, extra]
            payload_snapshots = {p: p.read_bytes() for run in preserved for p in run.glob("*.part")}
            if os.name == "nt":
                import ctypes
                from ctypes import wintypes
                kernel = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                               wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
                kernel.CreateFileW.restype = wintypes.HANDLE
                kernel.CloseHandle.argtypes = [wintypes.HANDLE]
                lock_run = owned_fixture(inbox)
                locked_part = next(lock_run.glob("*.part"))
                unlocked_part = lock_run / (".upload-" + uuid.uuid4().hex + ".part")
                unlocked_part.write_bytes(b"must survive failed preflight")
                payload_snapshots[unlocked_part] = b"must survive failed preflight"
                locked = kernel.CreateFileW(str(locked_part), 0x80000000, 0, None, 3, 0x80, None)
                assert locked != wintypes.HANDLE(-1).value, "Could not lock fixture"
                preserved.append(lock_run)
                redirect = inbox / (PREFIX + uuid.uuid4().hex)
                result = subprocess.run(["cmd", "/c", "mklink", "/J", str(redirect), str(external)], capture_output=True)
                assert result.returncode == 0, "Could not create test junction"
                junctions.append(redirect)
                preserved.append(redirect)
                payload_redirect = owned_fixture(inbox)
                redirected_part = next(payload_redirect.glob("*.part"))
                redirected_part.unlink()
                result = subprocess.run(["cmd", "/c", "mklink", "/J", str(redirected_part), str(external)], capture_output=True)
                assert result.returncode == 0, "Could not create payload junction"
                junctions.append(redirected_part)
                preserved.append(payload_redirect)
            snapshots = {p: (p / ".landrop-owner").read_bytes()
                         for p in preserved if (p / ".landrop-owner").is_file()}
            adversarial = Server(root / "adversarial-data", inbox); servers.append(adversarial)
            assert all(p.exists() for p in preserved), "Unsafe/foreign staging directory was removed"
            assert all((p / ".landrop-owner").read_bytes() == contents for p, contents in snapshots.items())
            assert all(p.read_bytes() == contents for p, contents in payload_snapshots.items())
            assert foreign.read_bytes() == b"legacy foreign data"
            assert sentinel.read_bytes() == b"external sentinel"
            assert (nested / "nested" / "keep.txt").read_bytes() == b"nested foreign data"
            assert (extra / "keep.txt").read_bytes() == b"unexpected foreign file"
            assert adversarial.request("/api/admin")["recoverySkippedRuns"] >= len(preserved)
            if locked is not None:
                kernel.CloseHandle(locked); locked = None
                assert locked_part.read_bytes() == b"synthetic incomplete fixture"
                assert unlocked_part.read_bytes() == b"must survive failed preflight"
                before = adversarial.request("/api/admin")["recoveredIncompleteUploads"]
                adversarial.folder(inbox)  # Explicitly retry the already selected folder.
                assert not lock_run.exists(), "Same-folder reselect must recover a formerly locked run"
                assert adversarial.request("/api/admin")["recoveredIncompleteUploads"] == before + 2
                preserved.remove(lock_run)
            adversarial.stop()
            assert all(p.exists() for p in preserved), "Shutdown removed foreign staging data"
            assert (inbox / final["name"]).read_bytes() == b"verified completed bytes"
            print("PASS: actual killed-upload recovery, verified final preservation, live lease, folder-switch admission, cancellation, checksum failure, graceful stop, foreign/malformed/nested/locked/reparse preservation.")
        finally:
            for conn in connections:
                conn.close()
            for server in servers:
                server.kill()
            if locked is not None:
                kernel.CloseHandle(locked)
            # Remove only fixture junction entries before TemporaryDirectory cleanup.
            for junction in reversed(junctions):
                if junction.exists():
                    os.rmdir(junction)


if __name__ == "__main__":
    main()
