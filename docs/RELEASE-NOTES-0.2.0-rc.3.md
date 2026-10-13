# LAN Drop 0.2.0-rc.3

This candidate makes HTTPS setup easier to diagnose. **Check certificate** shows local validity, matching IP addresses and expiry, with English/Czech explanations for missing input, invalid password/file, expired or mismatched certificates. **Start sharing** runs the same preflight and retains the server's independent validation. Sharing stops when periodic monitoring detects that its active certificate has expired.

After a restart, checking a generated certificate restores the adjacent public authority path and fingerprint only if its chain is verified locally. This does not install trust on the phone. Read [HTTPS setup](HTTPS.md) ([Czech guide](cs/HTTPS.md)). Changing the PC address or renewing a generated identity still requires a replacement certificate and explicit phone trust.

**Save anonymous diagnostics** manually saves a small JSON report containing versions, selected categories, certificate status and counts. It excludes names, paths, addresses, files, fingerprints, passwords, tokens and logs, and sends nothing. See [diagnostics](DIAGNOSTICS.md) ([česky](cs/DIAGNOSTICS.md)).

## Verification and limits

Automated certificate checks cover actionable failures, expiry, IP matching and rejection of unrelated companion authorities. Diagnostic tests enforce an explicit field whitelist and native checks exercise the same report path. Existing authorization, transfer integrity, storage, mobile browser emulation and trusted Chromium HTTPS checks remain in the CI suite.

This is a pre-release. Physical Android/iPhone certificate installation, actual phone HTTPS, QR cameras/file pickers, Wi-Fi/firewall/background behavior and clean Windows packaging remain unverified. See [release matrix](RELEASE-TEST-MATRIX.md) and [mobile simulation report](MOBILE-SIMULATION.md). The Windows x64 portable ZIP is self-contained and unsigned; its SHA-256 checksum verifies downloaded bytes, not publisher identity.

Extract the whole ZIP and run `LanDrop.Desktop.exe` with its `Server` folder beside it. Enter the certificate password again after restarting; it is never saved. English and Czech are included. Queues and activity remain current-run only; retry restarts unfinished files and browser downloads do not prove final phone storage.
