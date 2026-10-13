"""Actual HTTP checks for grouped consent, isolation, retry, and transfer receipts."""
import hashlib
import json
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
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    port = listener.getsockname()[1]
base = f"http://127.0.0.1:{port}"
storage = tempfile.TemporaryDirectory(prefix="landrop-batches-")
lines = queue.Queue()
process = subprocess.Popen([
    "dotnet", str(ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"),
    f"--Port={port}", "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
    f"--DataDirectory={storage.name}"], cwd=ROOT / "src/LanDrop",
    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
threading.Thread(target=lambda: [lines.put(line) for line in process.stdout], daemon=True).start()

def request(endpoint, method="GET", value=None, headers=None, expected=200, raw=False):
    headers = dict(headers or {})
    data = value if raw else json.dumps(value).encode() if value is not None else None
    if not raw:
        headers["Content-Type"] = "application/json"
    elif method == "PUT":
        headers.setdefault("X-LanDrop-SHA256", hashlib.sha256(data).hexdigest())
    try:
        result = urllib.request.urlopen(urllib.request.Request(base + endpoint, data, headers, method=method), timeout=15)
    except urllib.error.HTTPError as error:
        result = error
    body = result.read()
    assert result.status == expected, (endpoint, result.status, body)
    return json.loads(body) if body else None

try:
    deadline = time.monotonic() + 20
    key = None
    while time.monotonic() < deadline and not key:
        try:
            key = re.search(r"#admin=([a-f0-9]+)", lines.get(timeout=1))
        except queue.Empty:
            pass
    assert key, "Server startup failed"
    admin = {"X-LanDrop-Admin": key[1]}
    def pair(name):
        request("/api/admin/pairing", "POST", headers=admin)
        qr = request("/api/admin/qr?address=127.0.0.1", headers=admin)
        code = urllib.parse.parse_qs(urllib.parse.urlparse(qr["link"]).fragment)["pair"][0]
        phone = request("/api/pair", "POST", {"code":code,"name":name})
        device = next(d["id"] for d in request("/api/admin", headers=admin)["devices"] if d["name"] == name)
        request(f"/api/admin/devices/{device}/approve", "POST", headers=admin)
        return {"X-LanDrop-Session":phone["token"]}, device
    owner, device = pair("Batch owner")
    stranger, _ = pair("Other phone")
    inbox = Path(storage.name) / "Inbox"
    request("/api/admin/folder", "POST", {"path":str(inbox)}, admin)
    request("/api/offer-batches", "POST", {"files":[]}, expected=401)
    # Invalid tail must never leave valid earlier entries pending.
    for entries in [[], [{"name":"valid.txt","size":1},{"name":"../escape","size":1}],
                    [{"name":"negative.bin","size":-1}], [{"name":"no-size.bin"}],
                    [{"name":"too-big.bin","size":10 * 1024**3 + 1}]]:
        expected = 413 if entries and entries[0].get("size", 0) > 10 * 1024**3 else 400
        request("/api/offer-batches", "POST", {"files":entries}, owner, expected)
        state = request("/api/admin", headers=admin)
        assert state["batches"] == [] and state["offers"] == [], state
    payloads = [f"photo {i} – žluťoučký 🐎".encode() for i in range(20)]
    files = [{"name":f"photo-{i:02}.txt","size":len(body)} for i, body in enumerate(payloads)]
    batch = request("/api/offer-batches", "POST", {"files":files}, owner)
    assert batch["status"] == "pending" and len(batch["offers"]) == 20
    identifier = batch["id"]
    endpoint = f"/api/offer-batches/{identifier}"
    shown = next(b for b in request("/api/admin", headers=admin)["batches"] if b["id"] == identifier)
    assert shown["totalSize"] == sum(map(len, payloads)) and shown["deviceId"] == device
    assert len(shown["files"]) == 20
    assert request("/api/admin", headers=admin)["offers"] == [], "Grouped files must not appear as individual requests"
    request(endpoint, headers=stranger, expected=404)
    request(endpoint, "DELETE", headers=stranger, expected=404)
    first = batch["offers"][0]
    upload = "/api/files?" + urllib.parse.urlencode({"name":first["name"],"offer":first["id"]})
    request(upload, "PUT", payloads[0], owner, 409, raw=True)
    assert not list(inbox.glob("*.part")) and not list(inbox.rglob(".upload-*.part")), "No upload body may be staged before consent"
    for entry in inbox.iterdir():
        assert entry.is_dir() and entry.name.startswith(".landrop-incoming-"), "No completed file before consent"
        assert {p.name for p in entry.iterdir()} == {".landrop-owner", ".landrop-lease"}, "Only staging metadata before consent"
    request(f"/api/admin/offer-batches/{identifier}/approve", "POST", headers=owner, expected=401)
    request(f"/api/admin/offers/{first['id']}/approve", "POST", headers=admin, expected=409)
    request(f"/api/admin/offer-batches/{identifier}/approve", "POST", headers=admin)
    assert all(o["status"] == "approved" for o in request(endpoint, headers=owner)["offers"])
    request(upload, "PUT", payloads[0], stranger, 409, raw=True)
    request(upload.replace("photo-00", "renamed"), "PUT", payloads[0], owner, 400, raw=True)
    for offered, body in zip(batch["offers"], payloads):
        path = "/api/files?" + urllib.parse.urlencode({"name":offered["name"],"offer":offered["id"]})
        saved = request(path, "PUT", body, owner, raw=True)
        assert saved["verified"] and saved["sha256"] == hashlib.sha256(body).hexdigest()
        assert (inbox / saved["name"]).read_bytes() == body
        request(path, "PUT", body, owner, 409, raw=True)
    # Failed attempt consumes approval; a new batch can retry the same bytes.
    retry_body = b"retained selection"
    retry_file = {"name":"retry.txt","size":len(retry_body)}
    failed = request("/api/offer-batches", "POST", {"files":[retry_file]}, owner)
    request(f"/api/admin/offer-batches/{failed['id']}/approve", "POST", headers=admin)
    failed_path = "/api/files?" + urllib.parse.urlencode({"name":"retry.txt","offer":failed["offers"][0]["id"]})
    request(failed_path, "PUT", retry_body, {**owner,"X-LanDrop-SHA256":"0"*64}, 422, raw=True)
    assert not (inbox / "retry.txt").exists()
    failed_receipt = next(t for t in request("/api/transfers", headers=owner) if t["name"] == "retry.txt")
    assert failed_receipt["status"] == "failed" and failed_receipt["errorCode"] == "checksum_mismatch"
    retried = request("/api/offer-batches", "POST", {"files":[retry_file]}, owner)
    assert retried["id"] != failed["id"] and retried["status"] == "pending"
    request(f"/api/admin/offer-batches/{retried['id']}/approve", "POST", headers=admin)
    retry_path = "/api/files?" + urllib.parse.urlencode({"name":"retry.txt","offer":retried["offers"][0]["id"]})
    request(retry_path, "PUT", retry_body, owner, raw=True)
    assert (inbox / "retry.txt").read_bytes() == retry_body
    canceled = request("/api/offer-batches", "POST", {"files":[{"name":"cancel.txt","size":0}]}, owner)
    request(f"/api/offer-batches/{canceled['id']}", "DELETE", headers=owner)
    request(f"/api/admin/offer-batches/{canceled['id']}/approve", "POST", headers=admin, expected=409)
    rejected = request("/api/offer-batches", "POST", {"files":[{"name":"reject.txt","size":0}]}, owner)
    request(f"/api/admin/offer-batches/{rejected['id']}/reject", "POST", headers=admin)
    assert request(f"/api/offer-batches/{rejected['id']}", headers=owner)["status"] == "rejected"
    pending = [request("/api/offer-batches", "POST", {"files":[{"name":f"queued-{i}.txt","size":0}]}, owner) for i in range(32)]
    assert request("/api/offer-batches", "POST", {"files":[{"name":"over-limit.txt","size":0}]}, owner, 429)["code"] == "offer_limit"
    assert len(request("/api/admin", headers=admin)["batches"]) == 32
    for queued in pending:
        request(f"/api/offer-batches/{queued['id']}", "DELETE", headers=owner)
    # More than 200 actual receipts must evict old terminal transfer metadata.
    bounded_files = [{"name":f"bounded-{i}.txt","size":0} for i in range(205)]
    bounded = request("/api/offer-batches", "POST", {"files":bounded_files}, owner)
    request(f"/api/admin/offer-batches/{bounded['id']}/approve", "POST", headers=admin)
    for offered in bounded["offers"]:
        path = "/api/files?" + urllib.parse.urlencode({"name":offered["name"],"offer":offered["id"]})
        request(path, "PUT", b"", owner, raw=True)
    transfers = request("/api/transfers", headers=owner)
    assert transfers, "Owner should see actual transfers"
    assert request("/api/transfers", headers=stranger) == [], "Other devices cannot inspect receipts"
    assert len(transfers) == 200
    assert all(t["status"] == "completed" and t["bytesTransferred"] == t["size"] for t in transfers)
    assert len(request("/api/admin", headers=admin)["transfers"]) == 200
    request("/api/admin/shutdown", "POST", headers=admin)
    assert process.wait(timeout=10) == 0
    print("PASS: 20 files with one consent; atomic batch validation, metadata isolation, no pre-consent bytes, owner-only use, replay/mismatch denial, consumed-attempt retry, cancellation/rejection, 32-batch cap, terminal status/bytes, isolated 200-receipt bound.")
finally:
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
    storage.cleanup()
