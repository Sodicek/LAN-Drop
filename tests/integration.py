"""Real HTTP integration checks. Temporary inbox; server stopped after checks."""
import hashlib
import http.client
from concurrent.futures import ThreadPoolExecutor
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
    headers = dict(headers or {})
    if method == "PUT":
        headers.setdefault("X-LanDrop-SHA256", hashlib.sha256(data or b"").hexdigest())
        if headers["X-LanDrop-SHA256"] is None:
            del headers["X-LanDrop-SHA256"]
    req = urllib.request.Request(base + path, data=data, method=method, headers=headers)
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

def offer(name, size, session, approve=True):
    result = obj("/api/offers", "POST", {"name": name, "size": size}, session)
    assert result["status"] == "pending"
    if approve:
        obj(f"/api/admin/offers/{result['id']}/approve", "POST", headers=admin)
        assert obj(f"/api/offers/{result['id']}", headers=session)["status"] == "approved"
    return result["id"]

def upload(name, payload, session, expected=200):
    identifier = offer(name, len(payload), session)
    path = "/api/files?" + urllib.parse.urlencode({"name": name, "offer": identifier})
    result = request(path, "PUT", payload, session, expected)
    assert obj(f"/api/offers/{identifier}", headers=session)["status"] == "consumed"
    replay = json.loads(request(path, "PUT", payload, session, 409)[0])
    assert replay["code"] == "offer_not_approved", "Each approval authorizes exactly one attempt"
    if expected == 200:
        assert_verified(json.loads(result[0]), payload)
    return result

def assert_verified(value, payload):
    assert value["size"] == len(payload), value
    assert value["sha256"] == hashlib.sha256(payload).hexdigest(), value
    assert value["verified"] is True, value

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
    obj("/api/offers", "POST", {"name": "unauthorized.txt", "size": 0}, expected=401)
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
    obj("/api/offers", "POST", {"name": "pending.txt", "size": 0}, session, 401)
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
        # Pairing permits offers; each incoming file still needs explicit PC approval.
        failure = json.loads(request("/api/files?name=no-offer.txt", "PUT", b"x", session, 400)[0])
        assert failure["code"] == "offer_required"
        pending_offer = offer("approval.txt", 1, session, approve=False)
        request(f"/api/admin/offers/{pending_offer}/approve", "POST", b"", session, 401)
        offer_path = f"/api/files?name=approval.txt&offer={pending_offer}"
        failure = json.loads(request(offer_path, "PUT", b"x", session, 409)[0])
        assert failure["code"] == "offer_not_approved"
        assert not list(Path(directory).iterdir()), "No bytes may be saved before approval"
        pending_list = obj("/api/admin", headers=admin)["offers"]
        shown = next(item for item in pending_list if item["id"] == pending_offer)
        assert shown["name"] == "approval.txt" and shown["size"] == 1
        assert shown["deviceId"] == device and shown["deviceName"] == "Test iPhone"
        request(f"/api/offers/{pending_offer}", headers=session2, expected=404)
        request(f"/api/offers/{pending_offer}", "DELETE", headers=session2, expected=404)
        obj(f"/api/admin/offers/{pending_offer}/approve", "POST", headers=admin)
        failure = json.loads(request(offer_path, "PUT", b"x", session2, 409)[0])
        assert failure["code"] == "offer_not_approved"
        for mismatch_path, mismatch_body in [
            (f"/api/files?name=renamed.txt&offer={pending_offer}", b"x"),
            (offer_path, b"wrong length")]:
            failure = json.loads(request(mismatch_path, "PUT", mismatch_body, session, 400)[0])
            assert failure["code"] == "offer_mismatch"
        assert obj(f"/api/offers/{pending_offer}", headers=session)["status"] == "approved"
        obj(f"/api/admin/offers/{pending_offer}/reject", "POST", headers=admin)
        assert obj(f"/api/offers/{pending_offer}", headers=session)["status"] == "rejected"
        request(offer_path, "PUT", b"x", session, 409)
        canceled_offer = offer("canceled.txt", 1, session)
        request(f"/api/offers/{canceled_offer}", "DELETE", headers=session)
        assert obj(f"/api/offers/{canceled_offer}", headers=session)["status"] == "rejected"
        request(f"/api/files?name=canceled.txt&offer={canceled_offer}", "PUT", b"x", session, 409)
        assert not list(Path(directory).iterdir())
        for invalid in ["../escape.txt", "CON", "con.txt", "trailing."]:
            result = obj("/api/offers", "POST", {"name": invalid, "size": 1}, session, 400)
            assert result["code"] == "invalid_filename"
        obj("/api/offers", "POST", {"name": "negative.bin", "size": -1}, session, 400)
        request("/api/offers", "POST", b'{"name":"missing-size.bin"}',
                {**session, "Content-Type": "application/json"}, 400)
        obj("/api/offers", "POST", {"name": "huge.bin", "size": 10 * 1024**3 + 1}, session, 413)
        # Pending metadata is bounded independently of the stored-file cap.
        bounded_offers = [offer(f"queued-{index}.txt", 0, session, approve=False) for index in range(32)]
        assert obj("/api/offers", "POST", {"name": "over-offer-limit.txt", "size": 0}, session, 429)["code"] == "offer_limit"
        for identifier in bounded_offers:
            request(f"/api/offers/{identifier}", "DELETE", headers=session)
        # Invalid checksum metadata must not consume a valid approval or write any bytes.
        digest_offer = offer("digest.bin", 4, session)
        digest_path = f"/api/files?name=digest.bin&offer={digest_offer}"
        for digest in [None, "", "0" * 63, "0" * 65, "z" * 64, "0" * 32 + " " + "0" * 31]:
            failure = json.loads(request(digest_path, "PUT", b"good", {**session, "X-LanDrop-SHA256": digest}, 400)[0])
            assert failure["code"] == "checksum_required"
            assert obj(f"/api/offers/{digest_offer}", headers=session)["status"] == "approved"
            assert not (Path(directory)/"digest.bin").exists()
            assert not list(Path(directory).glob("*.part"))
        # Same-length corruption is caught by content hashing, not just file size.
        failure = json.loads(request(digest_path, "PUT", b"evil", {**session, "X-LanDrop-SHA256": hashlib.sha256(b"good").hexdigest()}, 422)[0])
        assert failure["code"] == "checksum_mismatch"
        assert obj(f"/api/offers/{digest_offer}", headers=session)["status"] == "consumed"
        assert json.loads(request(digest_path, "PUT", b"good", session, 409)[0])["code"] == "offer_not_approved"
        assert not (Path(directory)/"digest.bin").exists()
        assert not list(Path(directory).glob("*.part"))
        assert obj("/api/admin", headers=admin)["files"] == []
        outbound_path = f"/api/files?name=corrupted-outbound.bin&recipient={device}"
        for digest in [None, "broken"]:
            assert json.loads(request(outbound_path, "PUT", b"evil", {**admin, "X-LanDrop-SHA256": digest}, 400)[0])["code"] == "checksum_required"
        assert json.loads(request(outbound_path, "PUT", b"evil", {**admin, "X-LanDrop-SHA256": hashlib.sha256(b"good").hexdigest()}, 422)[0])["code"] == "checksum_mismatch"
        assert obj("/api/admin", headers=admin)["files"] == []
        assert not list((Path(storage.name)/"Outbox").glob("*/*.part"))
        assert not list((Path(storage.name)/"Outbox").glob("*/corrupted-outbound.bin"))
        payload = os.urandom(2 * 1024 * 1024 + 117)
        # Simultaneous requests must not claim the same approval twice.
        race_offer = offer("fotka žluťoučká.bin", len(payload), session)
        race_path = "/api/files?" + urllib.parse.urlencode({"name": "fotka žluťoučká.bin", "offer": race_offer})
        barrier = threading.Barrier(2)
        def race_upload(_):
            conn = http.client.HTTPConnection("127.0.0.1", port, timeout=10)
            try:
                barrier.wait(timeout=5)
                conn.request("PUT", race_path, payload, {**session, "X-LanDrop-SHA256": hashlib.sha256(payload).hexdigest()})
                response = conn.getresponse()
                return response.status, response.read()
            finally:
                conn.close()
        with ThreadPoolExecutor(max_workers=2) as pool:
            raced = list(pool.map(race_upload, range(2)))
        assert sorted(status for status, _ in raced) == [200, 409], raced
        assert json.loads(next(body for status, body in raced if status == 409))["code"] == "offer_not_approved"
        received = json.loads(next(body for status, body in raced if status == 200))
        assert_verified(received, payload)
        assert obj(f"/api/offers/{race_offer}", headers=session)["status"] == "consumed"
        assert (Path(directory)/received["name"]).read_bytes() == payload
        duplicate = json.loads(upload("fotka žluťoučká.bin",b"second",session)[0])
        assert duplicate["name"] != received["name"]
        assert (Path(directory)/received["name"]).read_bytes() == payload
        request("/api/files?name=..%2Fescape.txt","PUT",b"x",session,400)
        for invalid in ["CON", "con.txt", "PRN.pdf", "AUX", "NUL.bin", "COM1.txt", "LPT9.txt", "trailing.", "trailing "]:
            request("/api/files?name="+urllib.parse.quote(invalid),"PUT",b"x",session,400)
        empty = json.loads(upload("empty.txt",b"",session)[0])
        assert (Path(directory)/empty["name"]).stat().st_size == 0
        # A disconnected upload must leave neither a complete file nor a part file.
        conn = http.client.HTTPConnection("127.0.0.1",port,timeout=5)
        interrupted_offer = offer("interrupted.bin", 1000000, session)
        conn.putrequest("PUT",f"/api/files?name=interrupted.bin&offer={interrupted_offer}")
        conn.putheader("Content-Length","1000000")
        conn.putheader("X-LanDrop-Session",phone["token"])
        conn.putheader("X-LanDrop-SHA256", hashlib.sha256(b"x" * 1000000).hexdigest())
        conn.endheaders()
        conn.send(b"incomplete")
        conn.close()
        deadline = time.monotonic() + 5
        while list(Path(directory).glob("*.part")) and time.monotonic() < deadline:
            time.sleep(.05)
        assert not list(Path(directory).glob("*.part"))
        assert not (Path(directory)/"interrupted.bin").exists()
        request(f"/api/files?name=interrupted.bin&offer={interrupted_offer}", "PUT", b"x" * 1000000, session, 409)
        request("/api/files?name=missing-recipient.bin","PUT",payload,admin,400)
        request("/api/files?name=bad-recipient.bin&recipient=unknown","PUT",payload,admin,400)
        outbound = json.loads(request(f"/api/files?name=video.bin&recipient={device}","PUT",payload,admin)[0])
        outbound2 = json.loads(request(f"/api/files?name=other.bin&recipient={device2}","PUT",b"private second phone",admin)[0])
        assert_verified(outbound, payload)
        assert_verified(outbound2, b"private second phone")
        listed = obj("/api/state",headers=session)["files"]
        assert_verified(listed[0], payload)
        assert_verified(obj("/api/state", headers=session2)["files"][0], b"private second phone")
        all_metadata = {item["id"]: item for item in obj("/api/admin", headers=admin)["files"]}
        for receipt, content in [(received, payload), (duplicate, b"second"), (empty, b""), (outbound, payload), (outbound2, b"private second phone")]:
            assert_verified(all_metadata[receipt["id"]], content)
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
            upload(f"limit-{index}.txt",b"",session)
        failure = json.loads(upload("over-limit.txt",b"",session,429)[0])
        assert failure["code"] == "file_limit"
        assert not (Path(directory)/"over-limit.txt").exists()
        # Dismissing an inbound history record frees a record, never deletes received data.
        request("/api/admin/files/"+received["id"],"DELETE",headers=admin)
        assert (Path(directory)/received["name"]).read_bytes() == payload
        assert all(f["id"] != received["id"] for f in obj("/api/admin",headers=admin)["files"])
        upload("after-dismiss.txt",b"",session)
        revoked_offer = offer("revoked-offer.txt", 0, session, approve=False)
        obj(f"/api/admin/devices/{device}/revoke","POST",headers=admin)
        request(f"/api/offers/{revoked_offer}", headers=session, expected=401)
        expired = obj(f"/api/admin/offers/{revoked_offer}/approve", "POST", headers=admin, expected=409)
        assert expired["code"] == "offer_expired"
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
    print("PASS: SHA-256 receipts/metadata, missing/malformed digests, same-length corruption, cleanup, pairing, per-file approval, offer owner isolation, rejection/cancellation, mismatch, bounds, replay prevention, recipient isolation, file cap, ranges, revocation, graceful shutdown.")
finally:
    process.terminate()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
    storage.cleanup()
