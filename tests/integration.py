"""Real HTTP integration checks. Temporary inbox; server stopped after checks."""
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

ROOT = Path(__file__).resolve().parents[1]
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
base = f"http://127.0.0.1:{port}"
lines = queue.Queue()
storage = tempfile.TemporaryDirectory(prefix="landrop-data-")
process = subprocess.Popen(
    ["dotnet", str(ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"),
     f"--Port={port}", "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
     f"--DataDirectory={storage.name}", "--MaxRegisteredFiles=8"],
    cwd=ROOT / "src/LanDrop", stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    text=True, encoding="utf-8", errors="replace")
threading.Thread(target=lambda: [lines.put(line) for line in process.stdout], daemon=True).start()

def request(path, method="GET", data=None, headers=None, expected=200):
    req = urllib.request.Request(base + path, data=data, method=method, headers=headers or {})
    try:
        response = urllib.request.urlopen(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read()
    assert response.status == expected, (path, response.status, body)
    return body, response.headers

def obj(path, method="GET", value=None, headers=None, expected=200):
    data = json.dumps(value).encode() if value is not None else None
    body, response_headers = request(path, method, data, {**(headers or {}), "Content-Type":"application/json"}, expected)
    return json.loads(body) if body else None

try:
    deadline = time.monotonic() + 20
    admin_key = None
    while time.monotonic() < deadline:
        try:
            line = lines.get(timeout=1)
        except queue.Empty:
            continue
        match = re.search(r"#admin=([a-f0-9]+)", line)
        if match:
            admin_key = match[1]
            break
    assert admin_key, "Server did not start"
    admin = {"X-LanDrop-Admin": admin_key}
    request("/")
    request("/api/admin", expected=401)
    request("/api/state", expected=401)
    request("/api/files?name=unauthorized.txt", "PUT", b"blocked", expected=401)
    # Host rebinding defense.
    request("/api/admin", headers={**admin, "Host":"hostile.example"}, expected=400)
    state = obj("/api/admin", headers=admin)
    qr = obj("/api/admin/qr?address=127.0.0.1", headers=admin)
    assert "<svg" in qr["svg"]
    code = urllib.parse.parse_qs(urllib.parse.urlparse(qr["link"]).fragment)["pair"][0]
    obj("/api/pair","POST",{"code":"invalid","name":"test"},expected=400)
    stale = obj("/api/pair","POST",{"code":code,"name":"stale pending"})
    obj("/api/admin/pairing","POST",headers=admin)
    request("/api/state",headers={"X-LanDrop-Session":stale["token"]},expected=401)
    obj("/api/pair","POST",{"code":code,"name":"renewed QR"},expected=400)
    qr = obj("/api/admin/qr?address=127.0.0.1", headers=admin)
    code = urllib.parse.parse_qs(urllib.parse.urlparse(qr["link"]).fragment)["pair"][0]
    phone = obj("/api/pair","POST",{"code":code,"name":"Test iPhone"})
    competing = obj("/api/pair","POST",{"code":code,"name":"other pending"})
    session = {"X-LanDrop-Session":phone["token"]}
    assert not obj("/api/state",headers=session)["approved"]
    request("/api/files?name=pending.txt","PUT",b"blocked",session,401)
    device = next(d["id"] for d in obj("/api/admin",headers=admin)["devices"] if d["name"] == "Test iPhone")
    obj(f"/api/admin/devices/{device}/approve","POST",headers=admin)
    assert obj("/api/state",headers=session)["approved"]
    request("/api/state",headers={"X-LanDrop-Session":competing["token"]},expected=401)
    obj("/api/pair","POST",{"code":code,"name":"used QR"},expected=400)
    obj("/api/admin/pairing","POST",headers=admin)
    qr2 = obj("/api/admin/qr?address=127.0.0.1", headers=admin)
    code2 = urllib.parse.parse_qs(urllib.parse.urlparse(qr2["link"]).fragment)["pair"][0]
    phone2 = obj("/api/pair","POST",{"code":code2,"name":"Second iPhone"})
    session2 = {"X-LanDrop-Session":phone2["token"]}
    device2 = next(d["id"] for d in obj("/api/admin",headers=admin)["devices"] if d["name"] == "Second iPhone")
    pending2 = obj("/api/state",headers=session2)
    assert not pending2["approved"] and pending2["files"] == []
    request("/api/download-session","POST",b"",session2,401)
    request(f"/api/files?name=pending-recipient.txt&recipient={device2}","PUT",b"x",admin,400)
    obj(f"/api/admin/devices/{device2}/approve","POST",headers=admin)
    with tempfile.TemporaryDirectory(prefix="landrop-check-") as directory:
        obj("/api/admin/folder","POST",{"path":directory},admin)
        payload = os.urandom(2 * 1024 * 1024 + 117)
        path = "/api/files?name="+urllib.parse.quote("fotka žluťoučká.bin")
        received = json.loads(request(path,"PUT",payload,session)[0])
        assert (Path(directory)/received["name"]).read_bytes() == payload
        duplicate = json.loads(request(path,"PUT",b"second",session)[0])
        assert duplicate["name"] != received["name"]
        assert (Path(directory)/received["name"]).read_bytes() == payload
        request("/api/files?name=..%2Fescape.txt","PUT",b"x",session,400)
        for invalid in ["CON", "con.txt", "PRN.pdf", "AUX", "NUL.bin", "COM1.txt", "LPT9.txt", "trailing.", "trailing "]:
            request("/api/files?name="+urllib.parse.quote(invalid),"PUT",b"x",session,400)
        empty = json.loads(request("/api/files?name=empty.txt","PUT",b"",session)[0])
        assert (Path(directory)/empty["name"]).stat().st_size == 0
        # A disconnected upload must leave neither a complete file nor a part file.
        conn = http.client.HTTPConnection("127.0.0.1",port,timeout=5)
        conn.putrequest("PUT","/api/files?name=interrupted.bin")
        conn.putheader("Content-Length","1000000")
        conn.putheader("X-LanDrop-Session",phone["token"])
        conn.endheaders()
        conn.send(b"incomplete")
        conn.close()
        deadline = time.monotonic() + 5
        while list(Path(directory).glob("*.part")) and time.monotonic() < deadline:
            time.sleep(.05)
        assert not list(Path(directory).glob("*.part"))
        assert not (Path(directory)/"interrupted.bin").exists()
        request("/api/files?name=missing-recipient.bin","PUT",payload,admin,400)
        request("/api/files?name=bad-recipient.bin&recipient=unknown","PUT",payload,admin,400)
        outbound = json.loads(request(f"/api/files?name=video.bin&recipient={device}","PUT",payload,admin)[0])
        outbound2 = json.loads(request(f"/api/files?name=other.bin&recipient={device2}","PUT",b"private second phone",admin)[0])
        listed = obj("/api/state",headers=session)["files"]
        assert [f["id"] for f in listed] == [outbound["id"]]
        assert [f["id"] for f in obj("/api/state",headers=session2)["files"]] == [outbound2["id"]]
        request("/api/files/"+outbound["id"],headers=session2,expected=404)
        request("/api/files/"+outbound2["id"],headers=session,expected=404)
        assert request("/api/files/"+outbound["id"],headers=session)[0] == payload
        assert request("/api/files/"+outbound["id"],headers={**session,"Range":"bytes=0-31"},expected=206)[0] == payload[:32]
        _, cookie_headers = request("/api/download-session","POST",b"",session)
        cookie = {"Cookie":cookie_headers["Set-Cookie"].split(";")[0]}
        assert request("/download/"+outbound["id"],headers=cookie)[0] == payload
        _, cookie2_headers = request("/api/download-session","POST",b"",session2)
        cookie2 = {"Cookie":cookie2_headers["Set-Cookie"].split(";")[0]}
        request("/download/"+outbound["id"],headers=cookie2,expected=404)
        request("/download/"+outbound2["id"],headers=cookie,expected=404)
        assert request("/download/"+outbound2["id"],headers=cookie2)[0] == b"private second phone"
        request("/api/files/"+received["id"],headers=session,expected=404)
        # Five committed records above; three empty files must reach the configured cap.
        for index in range(3):
            request(f"/api/files?name=limit-{index}.txt","PUT",b"",session)
        failure = json.loads(request("/api/files?name=over-limit.txt","PUT",b"",session,429)[0])
        assert failure["code"] == "file_limit"
        assert not (Path(directory)/"over-limit.txt").exists()
        # Dismissing an inbound history record frees a record, never deletes received data.
        request("/api/admin/files/"+received["id"],"DELETE",headers=admin)
        assert (Path(directory)/received["name"]).read_bytes() == payload
        assert all(f["id"] != received["id"] for f in obj("/api/admin",headers=admin)["files"])
        request("/api/files?name=after-dismiss.txt","PUT",b"",session)
        obj(f"/api/admin/devices/{device}/revoke","POST",headers=admin)
        request("/api/files/"+outbound["id"],headers=session,expected=401)
        request("/download/"+outbound["id"],headers=cookie,expected=401)
        assert request("/download/"+outbound2["id"],headers=cookie2)[0] == b"private second phone"
        request(f"/api/files?name=revoked-recipient.bin&recipient={device}","PUT",b"x",admin,400)
        obj("/api/admin/pairing","POST",headers=admin)
        obj("/api/pair","POST",{"code":code,"name":"old QR"},expected=400)
        request("/api/admin/files/"+outbound["id"],"DELETE",headers=admin)
        request("/api/admin/shutdown","POST",b"",expected=401)
        assert process.poll() is None
        request("/api/admin/shutdown","POST",b"",admin)
        assert process.wait(timeout=10) == 0
        assert not list((Path(storage.name)/"Outbox").glob("*")), "Graceful shutdown must remove outbound copies"
        assert (Path(directory)/received["name"]).read_bytes() == payload
    print("PASS: pairing, pending invalidation, recipient isolation, uploads, file cap, safe history dismissal, cancellation, downloads, ranges, revocation, graceful shutdown.")
finally:
    process.terminate()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
    storage.cleanup()
