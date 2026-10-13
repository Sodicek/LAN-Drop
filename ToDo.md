# Roadmap

Current build: **0.2.0-rc.3**. A release candidate does not mean every release gate has passed; see `docs/RELEASE-TEST-MATRIX.md`.

## 0.1.1 — prototype hardening
- [x] Recipient-specific outbound lists, API access and Safari-cookie downloads.
- [x] Require an approved recipient for PC uploads.
- [x] Explicit selected-interface binding; loopback by default.
- [x] Pairing rotation/consumption and session revocation.
- [x] Upload concurrency and per-run storage quotas.
- [x] Empty files and reserved Windows names.
- [x] Two-device authorization tests.
- [x] Owned outbox crash cleanup, reparse-point checks and free-space checks.
- [ ] Incomplete inbox recovery after a crash and stronger atomic path handling.
- [ ] Physical iPhone: 20 photos to PC and a video back.
- [ ] Physical Android/Chrome: photos, videos, documents, Downloads and interrupted transfers.
- [x] Desktop start-time network profile gate.
- [x] Periodic desktop profile/address monitoring with automatic shutdown.
- [ ] Actual firewall and network-change testing on physical devices.

## 0.2.0 — Windows usability
- [x] Native WPF window and start/stop sharing.
- [x] Native folder/file pickers and recipient selection.
- [x] Persistent destination folder, interface preference and language.
- [x] Notification-area access; explicit hide vs shutdown.
- [x] English/Czech/Automatic and translation parity tests.
- [x] Android/iPhone browser guidance, phone override and upload capability checks.
- [x] Current-session transfer activity and manual retry of unfinished selections.
- [x] Connection recovery help for firewall, sleep and address changes.
- [x] Portable preview packaging script and Windows CI.
- [ ] Desktop interaction tests on a clean Windows machine.
- [x] Better server-start diagnosis and network profile information.
- [x] Automated desktop/server start, folder selection and shutdown check.
- [x] HTTPS default, local IP certificate creation/selection, session-only password and EN/CS trust setup help.
- [x] Certificate preflight, expiry warnings and verified public authority details after restart.
- [x] Strict TLS integration checks and Chromium/WebKit emulation with exact Unicode download names.
- [ ] Shared Core/Transport modules and typed client.
- [ ] Theme selection and full accessibility verification.

## 0.3.0 — reliable transfers
- [x] Upload speed and time remaining.
- [ ] Persistent queue/history.
- [ ] Repeat/retry and resumable chunked uploads.
- [x] Upload checksums, stored-file readback and matching completion receipts.
- [ ] Independently verified phone downloads and large-file performance measurements.
- [x] Concurrent disk-space reservation and owned outbox cleanup.
- [ ] Transfer restart recovery.

## 0.4.0 — offers and secure transport
- [x] Per-file receive offers with explicit approval, rejection and cancellation.
- [x] Batch offers for selecting many photos with one approval.
- [x] TLS backend with strict trusted-CA/hostname tests and HTTPS QR/cookie protection.
- [ ] Authenticated secure transport and manual authority setup proven on physical Android and iPhone Safari.
- [ ] Manual text/link sharing.
- [ ] Safe image previews and optional ZIP download after device tests.

## 0.5.0-beta — distribution
- [ ] Installer and code signing.
- [x] Manual diagnostic export with a strict anonymous field whitelist.
- [ ] Clean-machine portable/install/upgrade tests.
- [x] Portable ZIP/checksum packaging and a physical release-test matrix.
- [ ] Real Android/iOS/network compatibility matrix.

## 1.0.0 — stable
- [ ] Secure transport and update trust requirements satisfied.
- [ ] Supported platforms documented from real tests.
- [ ] No unresolved critical data-loss or authorization defects.
- [ ] Maintenance/support and localization contribution documentation.
