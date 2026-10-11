"""Real HTTPS checks with a temporary CA trusted only by this test's TLS client.

No root is installed on Windows, and no certificate/hostname validation is disabled.
The loopback HTTP listener is used solely as the desktop administration channel.
"""
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import socket
import ssl
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
PASSWORD = "test-only-password-0123456789"
DLL = ROOT / "src/LanDrop/bin/Release/net10.0/LanDrop.dll"

def available_port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]

with tempfile.TemporaryDirectory(prefix="landrop-https-") as temporary:
    temporary = Path(temporary)
    fixture = subprocess.run([
        "dotnet", "run", "--project", str(ROOT / "tests/Transport"), "-c", "Release", "--no-build",
        "--", "--generate", str(temporary / "server.pfx")],
        cwd=ROOT, check=True, text=True, encoding="utf-8", capture_output=True)
    certificate = json.loads(fixture.stdout.strip().splitlines()[-1])
    authority = Path(certificate["AuthorityPath"]).read_bytes()
    trusted = ssl.create_default_context(cadata=ssl.DER_cert_to_PEM_cert(authority))
    trusted.check_hostname = True
    assert trusted.verify_mode == ssl.CERT_REQUIRED
    port, https_port = available_port(), available_port()
    while port == https_port:
        https_port = available_port()
    admin_base = f"http://127.0.0.1:{port}"
    phone_base = f"https://127.0.0.1:{https_port}"
    environment = dict(os.environ)
    environment["LAN_DROP_CERTIFICATE_PASSWORD"] = PASSWORD
    environment["Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel"] = "Debug"
    # Credentials are accepted only from the dedicated environment variable.
    # Invalid HTTPS configuration must exit without leaving a plaintext server.
    for supplied_password in [None, "wrong-certificate-password"]:
        failure_environment = dict(environment)
        if supplied_password is None:
            failure_environment.pop("LAN_DROP_CERTIFICATE_PASSWORD", None)
        else:
            failure_environment["LAN_DROP_CERTIFICATE_PASSWORD"] = supplied_password
        failed_port = available_port()
        failure = subprocess.run([
            "dotnet", str(DLL), f"--Port={failed_port}", f"--HttpsPort={https_port}",
            "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
            f"--HttpsCertificatePath={certificate['CertificatePath']}",
            f"--HttpsCertificatePassword={PASSWORD}",
            f"--DataDirectory={temporary / 'invalid-data'}"],
            cwd=ROOT / "src/LanDrop", env=failure_environment,
            capture_output=True, text=True, encoding="utf-8", timeout=15)
        assert failure.returncode != 0, "Missing/wrong environment password started a server."
        error_output = failure.stdout + failure.stderr
        assert "HTTP fallback is disabled" in error_output, error_output
        assert PASSWORD not in error_output and "wrong-certificate-password" not in error_output
        with socket.socket() as connection:
            assert connection.connect_ex(("127.0.0.1", failed_port)) != 0, "Invalid TLS left an HTTP listener."
    unwanted_port = available_port()
    unwanted_configuration_port = available_port()
    environment["Kestrel__Endpoints__UnwantedPlaintext__Url"] = f"http://127.0.0.1:{unwanted_configuration_port}"
    lines = queue.Queue()
    output = []
    process = subprocess.Popen([
        "dotnet", str(DLL), f"--Port={port}", f"--HttpsPort={https_port}",
        "--OpenBrowser=false", "--ListenAddress=127.0.0.1",
        f"--urls=http://127.0.0.1:{unwanted_port}",
        f"--HttpsCertificatePath={certificate['CertificatePath']}",
        f"--DataDirectory={temporary / 'data'}"],
        cwd=ROOT / "src/LanDrop", env=environment, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    def read_output():
        for line in process.stdout:
            output.append(line)
            lines.put(line)
    threading.Thread(target=read_output, daemon=True).start()

    def request(endpoint, method="GET", value=None, headers=None, expected=200, raw=False, admin_channel=False):
        headers = dict(headers or {})
        body = value if raw else json.dumps(value).encode() if value is not None else None
        if not raw:
            headers["Content-Type"] = "application/json"
        elif method == "PUT":
            headers.setdefault("X-LanDrop-SHA256", hashlib.sha256(body).hexdigest())
        url = (admin_base if admin_channel else phone_base) + endpoint
        req = urllib.request.Request(url, body, headers, method=method)
        try:
            response = urllib.request.urlopen(req, context=trusted, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        content = response.read()
        assert response.status == expected, (endpoint, response.status, content)
        return (content if raw else json.loads(content) if content else None), response.headers

    def expect_validation_failure(host, context):
        try:
            with socket.create_connection(("127.0.0.1", https_port), timeout=5) as connection:
                with context.wrap_socket(connection, server_hostname=host):
                    pass
        except ssl.SSLCertVerificationError:
            return
        raise AssertionError("TLS unexpectedly accepted untrusted CA or wrong hostname: " + host)

    try:
        deadline = time.monotonic() + 25
        key = None
        while time.monotonic() < deadline and key is None:
            if process.poll() is not None:
                raise AssertionError("Server failed: " + "".join(output))
            try:
                match = re.search(r"#admin=([a-f0-9]+)", lines.get(timeout=1))
                if match:
                    key = match[1]
            except queue.Empty:
                pass
        assert key, "HTTPS server startup timed out: " + "".join(output)
        admin = {"X-LanDrop-Admin": key}
        for ignored_port in [unwanted_port, unwanted_configuration_port]:
            with socket.socket() as connection:
                assert connection.connect_ex(("127.0.0.1", ignored_port)) != 0, "Configuration added an unintended HTTP listener."
        # Successful chain + IP SAN validation, using a per-client trust context.
        with socket.create_connection(("127.0.0.1", https_port), timeout=5) as connection:
            with trusted.wrap_socket(connection, server_hostname="127.0.0.1") as encrypted:
                assert encrypted.version() in ("TLSv1.2", "TLSv1.3")
                assert encrypted.cipher()[2] >= 128
        expect_validation_failure("127.0.0.1", ssl.create_default_context())
        expect_validation_failure("localhost", trusted)
        # Sending plaintext to the phone port cannot return any HTTP content.
        with socket.create_connection(("127.0.0.1", https_port), timeout=5) as connection:
            connection.sendall(b"GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n")
            try:
                plaintext = connection.recv(1024)
            except ConnectionResetError:
                plaintext = b""
            assert not plaintext.startswith(b"HTTP/"), plaintext
        page, page_headers = request("/", raw=True)
        assert b"LAN Drop" in page
        qr, _ = request("/api/admin/qr?address=127.0.0.1", headers=admin, admin_channel=True)
        address = urllib.parse.urlparse(qr["link"])
        assert address.scheme == "https" and address.hostname == "127.0.0.1" and address.port == https_port, qr
        assert "<svg" in qr["svg"]
        code = urllib.parse.parse_qs(address.fragment)["pair"][0]
        phone, _ = request("/api/pair", "POST", {"code": code, "name": "HTTPS test phone"})
        session = {"X-LanDrop-Session": phone["token"]}
        request("/api/state", headers=session)
        state, _ = request("/api/admin", headers=admin, admin_channel=True)
        device = next(item["id"] for item in state["devices"] if item["name"] == "HTTPS test phone")
        request(f"/api/admin/devices/{device}/approve", "POST", headers=admin, admin_channel=True)
        inbox = temporary / "Inbox"
        request("/api/admin/folder", "POST", {"path": str(inbox)}, admin, admin_channel=True)
        payload = "HTTPS – žluťoučký Android / iPhone 🐎".encode() * 32768
        batch, _ = request("/api/offer-batches", "POST", {"files": [{"name": "secure-phone.bin", "size": len(payload)}]}, session)
        assert batch["status"] == "pending"
        request(f"/api/admin/offer-batches/{batch['id']}/approve", "POST", headers=admin, admin_channel=True)
        offer = batch["offers"][0]["id"]
        receipt, _ = request("/api/files?" + urllib.parse.urlencode({"name": "secure-phone.bin", "offer": offer}), "PUT", payload, session, raw=True)
        receipt = json.loads(receipt)
        assert receipt["verified"] is True and receipt["sha256"] == hashlib.sha256(payload).hexdigest()
        assert receipt["size"] == len(payload) and (inbox / receipt["name"]).read_bytes() == payload
        outgoing_name = "secure-pc-žluťoučký.bin"
        outgoing, _ = request("/api/files?" + urllib.parse.urlencode({"name": outgoing_name, "recipient": device}), "PUT", payload, admin, raw=True, admin_channel=True)
        outgoing = json.loads(outgoing)
        assert outgoing["verified"] and outgoing["sha256"] == hashlib.sha256(payload).hexdigest()
        _, cookie_headers = request("/api/download-session", "POST", b"", session, raw=True)
        cookie_text = cookie_headers["Set-Cookie"]
        assert re.search(r";\s*secure(?:;|$)", cookie_text, re.I), cookie_text
        assert "httponly" in cookie_text.lower() and "samesite=strict" in cookie_text.lower()
        cookie = {"Cookie": cookie_text.split(";")[0]}
        downloaded, download_headers = request("/download/" + outgoing["id"], headers=cookie, raw=True)
        assert downloaded == payload
        disposition = download_headers["Content-Disposition"]
        assert "filename*=UTF-8''".lower() in disposition.lower(), disposition
        assert "filename=" not in disposition.lower(), "Unicode names must not be degraded to an ASCII fallback."
        encoded_name = disposition.split("''", 1)[1]
        assert urllib.parse.unquote(encoded_name) == outgoing_name, disposition
        ranged, _ = request("/api/files/" + outgoing["id"], headers={**session, "Range": "bytes=0-31"}, raw=True, expected=206)
        assert ranged == payload[:32]
        request(f"/api/admin/devices/{device}/revoke", "POST", headers=admin, admin_channel=True)
        request("/api/state", headers=session, expected=401)
        request("/download/" + outgoing["id"], headers=cookie, expected=401, raw=True)
        request("/api/admin/shutdown", "POST", headers=admin, admin_channel=True)
        assert process.wait(timeout=15) == 0
        assert PASSWORD not in "".join(output), "Certificate password must never be logged."
        print("PASS: fail-closed TLS startup, env-only password, ignored extra HTTP endpoints, real CA-validated HTTPS, TLS protocol/cipher, untrusted CA and hostname rejection, plaintext rejection, HTTPS QR, secure cookie, approved SHA-256 upload/download/range and revoke.")
    except Exception:
        # Startup/handshake diagnostics are necessary to distinguish a platform
        # TLS provider failure from a failed assertion. Test credentials redact.
        print("".join(output).replace(PASSWORD, "[redacted]"))
        raise
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
