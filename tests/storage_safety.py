"""Windows storage integration checks; every server uses isolated temporary data."""
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
    def __init__(self, data, **config):
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        self.base = f"http://127.0.0.1:{port}"
        lines = queue.Queue()
        self.process = subprocess.Popen(
            ["dotnet", str(ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"),
             f"--Port={port}", "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
             f"--DataDirectory={data}", *[f"--{key}={value}" for key, value in config.items()]],
            cwd=ROOT / "src/LanDrop", stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace")
        threading.Thread(target=lambda: [lines.put(line) for line in self.process.stdout], daemon=True).start()
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            try:
                match = re.search(r"#admin=([a-f0-9]+)", lines.get(timeout=.5))
                if match:
                    self.admin = {"X-LanDrop-Admin": match[1]}
                    return
            except queue.Empty:
                if self.process.poll() is not None:
                    break
        self.kill()
        raise AssertionError("Storage test server did not start")

    def request(self, path, method="GET", body=None, headers=None, expected=200):
        request = urllib.request.Request(self.base + path, data=body, method=method, headers=headers or self.admin)
        try:
            response = urllib.request.urlopen(request, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            assert response.status == expected, (path, response.status, raw)
            return json.loads(raw) if raw else None

    def folder(self, path, expected=200):
        return self.request("/api/admin/folder", "POST", json.dumps({"path": str(path)}).encode(),
                            {**self.admin, "Content-Type": "application/json"}, expected)

    def phone(self):
        qr = self.request("/api/admin/qr?address=127.0.0.1")
        code = urllib.parse.parse_qs(urllib.parse.urlparse(qr["link"]).fragment)["pair"][0]
        token = self.request("/api/pair", "POST", json.dumps({"code": code, "name": "Storage test"}).encode(),
                             {"Content-Type": "application/json"})["token"]
        device = self.request("/api/admin")["devices"][0]["id"]
        self.request(f"/api/admin/devices/{device}/approve", "POST", b"")
        return {"X-LanDrop-Session": token}, device

    def stop(self):
        if self.process.poll() is None:
            self.request("/api/admin/shutdown", "POST", b"")
            assert self.process.wait(timeout=10) == 0

    def kill(self):
        if self.process.poll() is None:
            self.process.kill()
            self.process.wait(timeout=10)


with tempfile.TemporaryDirectory(prefix="landrop-storage-") as directory:
    root = Path(directory)
    data, inbox = root / "data", root / "inbox"
    data.mkdir(); inbox.mkdir()
    (data / "settings.json").write_text(json.dumps({"Path": str(inbox)}))
    sentinel = data / "Outbox" / "unmarked"
    sentinel.mkdir(parents=True)
    (sentinel / "keep.txt").write_text("not owned by LAN Drop")
    servers = []
    junction = root / "junction"
    try:
        live = Server(data); servers.append(live)
        phone, recipient = live.phone()
        received = live.request("/api/files?name=keep-inbound.txt", "PUT", b"received data", phone)
        live.request(f"/api/files?name=live-outbound.txt&recipient={recipient}", "PUT", b"live copy")
        live_runs = set((data / "Outbox").iterdir()) - {sentinel}
        assert len(live_runs) == 1
        live_run = live_runs.pop()

        crashed = Server(data); servers.append(crashed)
        _, recipient2 = crashed.phone()
        crashed.request(f"/api/files?name=stale-outbound.txt&recipient={recipient2}", "PUT", b"stale copy")
        stale_runs = set((data / "Outbox").iterdir()) - {sentinel, live_run}
        assert len(stale_runs) == 1
        stale_run = stale_runs.pop()
        crashed.kill()
        assert stale_run.exists()

        cleaner = Server(data); servers.append(cleaner)
        assert not stale_run.exists(), "Owned crashed outbox should be cleaned"
        assert (live_run / "live-outbound.txt").read_bytes() == b"live copy"
        assert (sentinel / "keep.txt").read_text() == "not owned by LAN Drop"
        assert (inbox / received["name"]).read_bytes() == b"received data"

        target = root / "target"; target.mkdir()
        result = subprocess.run(["cmd", "/c", "mklink", "/J", str(junction), str(target)], capture_output=True)
        assert result.returncode == 0, result.stderr
        assert cleaner.folder(junction, 400)["code"] == "unsafe_folder"
        assert cleaner.folder(junction / "nested", 400)["code"] == "unsafe_folder"
        assert not (target / "nested").exists(), "Rejected path must not create directories beyond junction"

        # Revalidate a selected folder at upload admission, not just in the picker.
        selected = root / "selected"; selected.mkdir()
        cleaner.folder(selected)
        selected.rmdir()
        result = subprocess.run(["cmd", "/c", "mklink", "/J", str(selected), str(target)], capture_output=True)
        assert result.returncode == 0, result.stderr
        try:
            cleanerphone, _ = cleaner.phone()
            assert cleaner.request("/api/files?name=redirected.bin", "PUT", b"x", cleanerphone, 400)["code"] == "unsafe_folder"
            assert not (target / "redirected.bin").exists()
        finally:
            os.rmdir(selected)
            selected.mkdir()
            cleaner.folder(inbox)

        disk = Server(data, DiskSafetyBufferBytes=9223372036854775807); servers.append(disk)
        diskphone, _ = disk.phone()
        error = disk.request("/api/files?name=no-space.bin", "PUT", b"x", diskphone, 507)
        assert error["code"] == "insufficient_disk_space"
        assert not (inbox / "no-space.bin").exists()
        assert not list(inbox.glob("*.part"))
        disk.stop(); cleaner.stop(); live.stop()
        assert (inbox / received["name"]).read_bytes() == b"received data"
        assert (sentinel / "keep.txt").exists()
        assert set((data / "Outbox").iterdir()) == {sentinel}
        print("PASS: junction ancestors rejected, disk admission, stale cleanup, live/unmarked preservation, inbound preservation.")
    finally:
        for server in servers:
            server.kill()
        if junction.exists():
            os.rmdir(junction)  # Removes junction itself, never its target.
