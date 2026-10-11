# Roadmap

Current build: **0.2.0-alpha.4**. An alpha number does not mean every release gate has passed.

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
- [x] Desktop start-time network profile gate.
- [x] Periodic desktop profile/address monitoring with automatic shutdown.
- [ ] Actual firewall and network-change testing on physical devices.

## 0.2.0 — Windows usability
- [x] Native WPF window and start/stop sharing.
- [x] Native folder/file pickers and recipient selection.
- [x] Persistent destination folder, interface preference and language.
- [x] Notification-area access; explicit hide vs shutdown.
- [x] English/Czech/Automatic and translation parity tests.
- [x] Portable preview packaging script and Windows CI.
- [ ] Desktop interaction tests on a clean Windows machine.
- [x] Better server-start diagnosis and network profile information.
- [x] Automated desktop/server start, folder selection and shutdown check.
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
- [ ] Batch offers for selecting many photos with one approval.
- [ ] Authenticated secure transport proven in Safari or revised native iOS scope.
- [ ] Manual text/link sharing.
- [ ] Safe image previews and optional ZIP download after device tests.

## 0.5.0-beta — distribution
- [ ] Installer and code signing.
- [ ] Diagnostic export with redaction.
- [ ] Clean-machine portable/install/upgrade tests.
- [ ] Real iOS/network compatibility matrix.

## 1.0.0 — stable
- [ ] Secure transport and update trust requirements satisfied.
- [ ] Supported platforms documented from real tests.
- [ ] No unresolved critical data-loss or authorization defects.
- [ ] Maintenance/support and localization contribution documentation.
