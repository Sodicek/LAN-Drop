"""Actual server checks for opt-in, metadata-only completion history persistence."""
import hashlib
import http.client
from datetime import datetime, timezone
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

ROOT = Path(__file__).resolve().parents[1]


class Server:
    def __init__(self, data, development=False, **config):
        data = Path(data)
        data.mkdir(parents=True, exist_ok=True)
        if not (data / "settings.json").exists():
            (data / "settings.json").write_text(json.dumps({"Path": str(data / "Inbox")}))
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        self.base = f"http://127.0.0.1:{port}"
        environment = os.environ.copy()
        if development:
            environment["ASPNETCORE_ENVIRONMENT"] = "Development"
        lines = queue.Queue()
        self.process = subprocess.Popen([
            "dotnet", str(ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"),
            f"--Port={port}", "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
            f"--DataDirectory={data}", *[f"--{key}={value}" for key, value in config.items()]], cwd=ROOT / "src/LanDrop", env=environment,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
            encoding="utf-8", errors="replace")
        threading.Thread(target=lambda: [lines.put(line) for line in self.process.stdout], daemon=True).start()
        self.admin = None
        deadline = time.monotonic() + 25
        while time.monotonic() < deadline and not self.admin:
            try:
                match = re.search(r"#admin=([a-f0-9]+)", lines.get(timeout=.5))
                if match:
                    self.admin = {"X-LanDrop-Admin": match[1]}
            except queue.Empty:
                if self.process.poll() is not None:
                    break
        if not self.admin:
            self.stop()
            raise AssertionError("History fixture server did not start")

    def request(self, path, method="GET", value=None, headers=None, expected=200, raw=False):
        headers = dict(headers or {})
        data = value if raw else json.dumps(value).encode() if value is not None else None
        if not raw:
            headers["Content-Type"] = "application/json"
        elif method == "PUT":
            headers.setdefault("X-LanDrop-SHA256", hashlib.sha256(data).hexdigest())
        try:
            response = urllib.request.urlopen(urllib.request.Request(self.base + path, data, headers, method=method), timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read()
        assert response.status == expected, (path, response.status, body)
        return body if raw else json.loads(body) if body else None

    def pair(self, name="Private device name must never persist"):
        self.request("/api/admin/pairing", "POST", headers=self.admin)
        qr = self.request("/api/admin/qr?address=127.0.0.1", headers=self.admin)
        code = urllib.parse.parse_qs(urllib.parse.urlparse(qr["link"]).fragment)["pair"][0]
        phone = self.request("/api/pair", "POST", {"code": code, "name": name})
        device = next(d["id"] for d in self.request("/api/admin", headers=self.admin)["devices"] if d["name"] == name)
        self.request(f"/api/admin/devices/{device}/approve", "POST", headers=self.admin)
        return {"X-LanDrop-Session": phone["token"]}, device

    def upload(self, name, payload, session, approve=True, checksum=None, expected=200):
        offer = self.request("/api/offers", "POST", {"name": name, "size": len(payload)}, session)
        if approve:
            self.request(f"/api/admin/offers/{offer['id']}/approve", "POST", headers=self.admin)
        headers = dict(session)
        if checksum:
            headers["X-LanDrop-SHA256"] = checksum
        receipt = self.request("/api/files?" + urllib.parse.urlencode({"name": name, "offer": offer["id"]}),
                               "PUT", payload, headers, expected, raw=True)
        return json.loads(receipt)

    def outgoing(self, name, payload, device):
        return json.loads(self.request("/api/files?" + urllib.parse.urlencode({"name": name, "recipient": device}),
                                       "PUT", payload, self.admin, raw=True))

    def stop(self):
        if self.process.poll() is None:
            try:
                if self.admin:
                    self.request("/api/admin/shutdown", "POST", headers=self.admin)
                    self.process.wait(timeout=10)
                else:
                    self.process.kill()
            finally:
                if self.process.poll() is None:
                    self.process.kill()
                self.process.wait(timeout=10)
        if self.process.stdout:
            self.process.stdout.close()


def main():
    servers = []
    redirects = []
    with tempfile.TemporaryDirectory(prefix="landrop-history-") as directory:
        root = Path(directory)
        try:
            public = root / "public-assets"; public.mkdir()
            probe = public / "probe.json"; probe.write_bytes(b'{"public":true}')
            isolated = Server(root / "private-assets-test", webroot=public); servers.append(isolated)
            assert isolated.request("/probe.json", raw=True) == probe.read_bytes(), "Fixture must exercise the actual public web root"
            for destination in (public, public / "nested", root):
                rejected = isolated.request("/api/admin/folder", "POST", {"path": str(destination)}, isolated.admin, expected=400)
                assert rejected["code"] == "unsafe_folder"
            assert not (public / "nested").exists()
            isolated.request("/api/admin/history/settings", "POST", {"enabled": True}, isolated.admin)
            isolated.request("/History/history.json", expected=404, raw=True)
            isolated.stop()
            development = Server(root / "private-development", development=True, webroot=public); servers.append(development)
            assert development.request("/probe.json", raw=True) == probe.read_bytes()
            development.request("/app.js", expected=404, raw=True)
            development.stop()
            for unsafe_data in (public, public / "private-data", root):
                with socket.socket() as listener:
                    listener.bind(("127.0.0.1", 0)); bad_port = listener.getsockname()[1]
                rejected = subprocess.run(["dotnet", str(ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"),
                    f"--Port={bad_port}", "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
                    f"--DataDirectory={unsafe_data}", f"--webroot={public}"], cwd=ROOT / "src/LanDrop",
                    capture_output=True, timeout=20)
                assert rejected.returncode != 0, "Private data must not overlap public assets"
                with socket.socket() as listener:
                    assert listener.connect_ex(("127.0.0.1", bad_port)) != 0
            assert sorted(p.name for p in public.iterdir()) == ["probe.json"], "Rejected storage must not publish metadata or create folders"
            data = root / "normal"
            server = Server(data); servers.append(server)
            session, device = server.pair()
            def history(target=server):
                return target.request("/api/admin/history", headers=target.admin)
            def settings(enabled, target=server, expected=200):
                return target.request("/api/admin/history/settings", "POST", {"enabled": enabled}, target.admin, expected)
            def clear(target=server, expected=200):
                return target.request("/api/admin/history", "DELETE", headers=target.admin, expected=expected)
            assert history()["available"] is True and history()["enabled"] is False
            assert history()["entries"] == []
            for headers in ({}, session):
                server.request("/api/admin/history", headers=headers, expected=401)
                server.request("/api/admin/history/settings", "POST", {"enabled": True}, headers, 401)
                server.request("/api/admin/history", "DELETE", headers=headers, expected=401)
            server.request("/api/admin/history/settings", "POST", {}, server.admin, 400)
            disabled_name = "disabled-private-name.txt"
            disabled = server.upload(disabled_name, b"default-off bytes", session)
            assert disabled["verified"] is True
            assert history()["entries"] == []
            history_path = data / "History" / "history.json"
            assert not history_path.exists() or disabled_name not in history_path.read_text(), "Default-off completion leaked metadata to disk"
            settings(True)
            assert history()["enabled"] is True

            # Pending, rejected, malformed checksum, and disconnected attempts never become completion history.
            server.upload("pending.bin", b"p", session, approve=False, expected=409)
            rejected = server.request("/api/offers", "POST", {"name": "rejected.bin", "size": 1}, session)
            server.request(f"/api/offers/{rejected['id']}", "DELETE", headers=session)
            server.request("/api/files?" + urllib.parse.urlencode({"name": "rejected.bin", "offer": rejected["id"]}),
                           "PUT", b"r", session, 409, raw=True)
            failure = server.upload("checksum.bin", b"bad", session, checksum="0" * 64, expected=422)
            assert failure["code"] == "checksum_mismatch"
            offer = server.request("/api/offers", "POST", {"name": "interrupted.bin", "size": 1000000}, session)
            server.request(f"/api/admin/offers/{offer['id']}/approve", "POST", headers=server.admin)
            address = urllib.parse.urlparse(server.base)
            connection = http.client.HTTPConnection(address.hostname, address.port, timeout=10)
            try:
                connection.putrequest("PUT", "/api/files?" + urllib.parse.urlencode({"name": "interrupted.bin", "offer": offer["id"]}))
                for key, value in {**session, "Content-Length": "1000000", "X-LanDrop-SHA256": "0" * 64}.items():
                    connection.putheader(key, value)
                connection.endheaders(); connection.send(b"x" * 4096)
                deadline = time.monotonic() + 5
                while not list(data.rglob("*.part")) and time.monotonic() < deadline:
                    time.sleep(.03)
                assert list(data.rglob("*.part")), "Interrupted history fixture never stored partial bytes"
            finally:
                connection.close()
            deadline = time.monotonic() + 5
            while list(data.rglob("*.part")) and time.monotonic() < deadline:
                time.sleep(.03)
            assert not list(data.rglob("*.part"))
            assert history()["entries"] == []

            payload = "Verified Czech bytes: příliš žluťoučký".encode()
            received = server.upload("český soubor.txt", payload, session)
            assert received["sha256"] == hashlib.sha256(payload).hexdigest() and received["verified"] is True
            outgoing = server.outgoing("outgoing.bin", b"download bytes", device)
            entries = history()["entries"]
            assert len(entries) == 2
            assert [(e["name"], e["direction"], e["size"]) for e in entries] == [
                (outgoing["name"], "outgoing", outgoing["size"]), (received["name"], "incoming", received["size"])]
            for entry in entries:
                assert set(entry) == {"id", "name", "size", "direction", "completedAt"}, entry
                timestamp = datetime.fromisoformat(entry["completedAt"].replace("Z", "+00:00"))
                assert abs((datetime.now(timezone.utc) - timestamp).total_seconds()) < 60
            stored = json.loads(history_path.read_text(encoding="utf-8"))
            assert set(stored) == {"version", "enabled", "entries"}
            assert stored["version"] == 1 and stored["entries"] == entries
            disk_text = history_path.read_text(encoding="utf-8")
            for secret in [session["X-LanDrop-Session"], server.admin["X-LanDrop-Admin"],
                           str(data), "Private device name must never persist", received["sha256"], device]:
                assert secret not in disk_text, "Persistent history leaked a capability, identity, checksum, or path"
            assert "history" not in server.request("/api/state", headers=session)
            saved_bytes = data / "Inbox" / received["name"]
            assert saved_bytes.read_bytes() == payload

            settings(False)
            server.upload("disabled-again.txt", b"unrecorded", session)
            assert history()["entries"] == entries and history()["enabled"] is False
            server.stop()
            restored = Server(data); servers.append(restored)
            assert history(restored)["entries"] == entries and history(restored)["enabled"] is False
            assert restored.request("/api/admin", headers=restored.admin)["files"] == []
            restored.request("/api/state", headers=session, expected=401)
            restored.request("/api/files/" + outgoing["id"], headers=session, expected=401)
            assert saved_bytes.read_bytes() == payload
            fresh_session, fresh_device = restored.pair("Fresh session")
            restored.request("/api/files/" + outgoing["id"], headers=fresh_session, expected=404)
            live_outgoing = restored.outgoing("live.bin", b"live after history clear", fresh_device)
            settings(True, restored)
            clear(restored)
            assert history(restored)["entries"] == [] and history(restored)["enabled"] is True
            assert saved_bytes.read_bytes() == payload
            assert restored.request("/api/files/" + live_outgoing["id"], headers=fresh_session, raw=True) == b"live after history clear"

            # A genuine sequence of successful transfers exercises trimming, ordering, and atomic persisted state.
            for index in range(201):
                receipt = restored.upload(f"bounded-{index:03}.txt", bytes([index % 256]), fresh_session)
                assert receipt["verified"] is True
            bounded = history(restored)["entries"]
            assert len(bounded) == 200 and bounded[0]["name"] == "bounded-200.txt" and bounded[-1]["name"] == "bounded-001.txt"
            assert len({e["id"] for e in bounded}) == 200
            assert json.loads(history_path.read_text(encoding="utf-8"))["entries"] == bounded
            assert not (history_path.parent / ".checkpoint").exists(), "Successful atomic commits left a temporary checkpoint"

            contender = Server(data); servers.append(contender)
            assert history(contender)["available"] is False
            assert settings(True, contender, 409)["code"] == "history_unavailable"
            assert clear(contender, 409)["code"] == "history_unavailable"
            other_session, _ = contender.pair("Second live process")
            other_receipt = contender.upload("lease-collision.txt", b"sharing still works", other_session)
            assert other_receipt["verified"] is True
            assert json.loads(history_path.read_text(encoding="utf-8"))["entries"] == bounded
            contender.stop(); restored.stop()
            after_bound = Server(data); servers.append(after_bound)
            assert history(after_bound)["entries"] == bounded
            clear(after_bound); after_bound.stop()
            final = Server(data); servers.append(final)
            assert history(final)["entries"] == [] and history(final)["enabled"] is True
            assert saved_bytes.read_bytes() == payload
            final.stop()

            blocked_data = root / "blocked-checkpoint"
            blocked = Server(blocked_data); servers.append(blocked)
            settings(True, blocked)
            checkpoint = blocked_data / "History" / ".checkpoint"
            checkpoint.write_bytes(b"foreign interrupted checkpoint")
            prior_store = (blocked_data / "History" / "history.json").read_bytes()
            blocked_session, _ = blocked.pair()
            assert blocked.upload("checkpoint-blocked.txt", b"receipt survives persistence failure", blocked_session)["verified"] is True
            assert history(blocked)["available"] is False
            assert checkpoint.read_bytes() == b"foreign interrupted checkpoint"
            assert (blocked_data / "History" / "history.json").read_bytes() == prior_store
            assert settings(False, blocked, 409)["code"] == "history_unavailable"
            blocked.stop()
            still_blocked = Server(blocked_data); servers.append(still_blocked)
            assert history(still_blocked)["available"] is False
            assert checkpoint.read_bytes() == b"foreign interrupted checkpoint"
            still_blocked.stop()

            # Bad stores remain byte-for-byte intact and cannot silently become trusted metadata.
            invalid_documents = [b"{broken", b"{" + b" " * (2 * 1024 * 1024) + b"}",
                b'{"version":99,"enabled":true,"entries":[]}',
                b'{"version":1,"enabled":true,"entries":[{"id":"bad","name":"../escape","size":1,"direction":"incoming","completedAt":"2026-01-01T00:00:00Z"}]}']
            for index, contents in enumerate(invalid_documents):
                damaged = root / f"damaged-{index}"
                store = damaged / "History"; store.mkdir(parents=True)
                path = store / "history.json"; path.write_bytes(contents)
                target = Server(damaged); servers.append(target)
                assert history(target)["available"] is False and history(target)["enabled"] is False
                assert settings(True, target, 409)["code"] == "history_unavailable"
                assert clear(target, 409)["code"] == "history_unavailable"
                usable_session, _ = target.pair()
                assert target.upload("without-history.txt", b"still transferable", usable_session)["verified"] is True
                target.stop()
                assert path.read_bytes() == contents, "Corrupt metadata was overwritten or removed"

            outside = root / "external"; outside.mkdir()
            sentinel = outside / "history.json"; sentinel.write_bytes(b"outside sentinel")
            redirected_data = root / "redirected"; redirected_data.mkdir()
            redirect = redirected_data / "History"
            if os.name == "nt":
                result = subprocess.run(["cmd", "/c", "mklink", "/J", str(redirect), str(outside)], capture_output=True)
                assert result.returncode == 0, "Could not create isolated history junction fixture"
            else:
                redirect.symlink_to(outside, target_is_directory=True)
            redirects.append(redirect)
            redirected = Server(redirected_data); servers.append(redirected)
            assert history(redirected)["available"] is False
            assert settings(True, redirected, 409)["code"] == "history_unavailable"
            assert clear(redirected, 409)["code"] == "history_unavailable"
            redirected.stop()
            assert sentinel.read_bytes() == b"outside sentinel"
            assert sorted(p.name for p in outside.iterdir()) == ["history.json"], "History touched a reparse target"
            print("PASS: default-off/private/admin-only history, verified completion metadata, failed/pending/disconnected isolation, disable/restart, no capability restoration, clear without payload/grant deletion, 200-record bound, writer lease collision, corruption/reparse preservation.")
        finally:
            for target in reversed(servers):
                target.stop()
            for redirect in redirects:
                # Remove the exact fixture entry before temporary-tree cleanup; never recurse into its target.
                if os.name == "nt":
                    os.rmdir(redirect)
                else:
                    redirect.unlink()


if __name__ == "__main__":
    main()
