# Changelog

## 0.2.0-alpha.5 — 2026-10-11

### Added
- Android Chrome and iPhone Safari guidance in the shared browser client.
- Automatic phone detection, a persistent manual override and localized suggested phone names.
- Browser upload capability checks with readable fallback instructions; download access remains available.
- English/Czech Android setup guides and an expanded physical-device test matrix.
- Chromium Android emulation tests for pairing, checksum-worker uploads, acceptance/cancellation and Unicode downloads.

### Changed
- Phone-facing branding and desktop instructions now cover Android and iPhone.
- Device naming preserves user-entered names when platform or language changes.

### Known gaps
- Physical Android/iPhone camera, file-picker, download and background tests remain pending.
- This is a browser client, with no APK or guaranteed background transfer. HTTP remains unencrypted.

## 0.2.0-alpha.4 — 2026-10-11

### Added
- SHA-256 integrity checks from the sender through streaming upload and stored-file readback before commit.
- Browser preparation in a cancelable worker with one-megabyte file slices; native asynchronous preparation keeps the source open through upload.
- Strict upload receipts: success requires matching size, checksum and verified status.
- English/Czech preparation and checked-PC-copy labels.
- Known-vector, worker cancellation, malformed receipt, corruption, cleanup and real desktop upload/download tests.

### Changed
- Every upload requires `X-LanDrop-SHA256`; old/custom clients must update.
- Pinned browser hash dependency, local bundled assets and reproducible bundle verification in CI.
- Portable packages use versioned folders to avoid overwriting a running older preview.

### Known gaps
- Checksums detect accidental corruption; HTTP still allows an attacker to replace data and checksums.
- Verification applies to the saved PC copy at commit time, not Safari's downloaded copy or future edits.
- Preparing and reading back files adds disk/CPU work; large-file and physical iPhone benchmarks are pending.

## 0.2.0-alpha.3 — 2026-10-11

### Added
- Per-file receive approval on the PC before a phone sends any file body.
- English/Czech pending request panels in Windows and browser administration, with accept/reject controls.
- Cancelable phone waiting flow, ten-minute offers and one-use authorization bound to device, file name and size.
- Desktop monitoring every fifteen seconds stops sharing when the selected address disappears or its profile becomes public/unknown.
- Offer ownership, rejection, cancellation, replay and client cancellation-race tests.

### Changed
- Incoming HTTP uploads now require an approved offer; older clients must implement the offer protocol.
- Browser control requests have a fifteen-second deadline so connection loss cannot leave approval waiting indefinitely.

### Known gaps
- HTTP remains unencrypted; approval is not transport security or proof of file contents.
- Network monitoring is periodic and desktop-only; a profile change may be detected after a delay.
- Offers are per file and are not persisted. A failed attempted transfer needs a new approval.

## 0.2.0-alpha.2 — 2026-10-11

### Added
- Read-only Windows adapter/profile diagnostics; desktop sharing blocks public and unknown profiles.
- Upload speed and estimated time remaining in English and Czech.
- Free-space admission checks, concurrent disk reservations and a safety buffer.
- Reparse-point checks for inbox paths and cleanup of owned inactive outboxes after a crash.
- Storage safety, network policy, progress and real desktop/server lifecycle checks in CI.

### Fixed
- Desktop server startup resolves bundled web assets from an absolute content root.
- Startup errors remain visible after cleanup; browser uploads wait for the server's save response.

### Known gaps
- HTTP remains unencrypted; physical phone, firewall and clean-machine checks are pending.
- Incomplete inbox files after a crash are not automatically recovered or removed.
- Network profiles are checked before desktop sharing starts, not continuously while it runs.

## 0.2.0-alpha.1 — 2026-10-11

Development preview; no stable-release claim.

### Added
- Native Windows WPF interface, folder/file pickers, notification-area access.
- English and Czech UI with automatic-language option and extensible dictionaries.
- Per-recipient outgoing files and two-device authorization checks.
- Selected-interface binding, persistent destination folder and explicit sharing controls.
- Empty-file support, Windows reserved-name checks, concurrency/storage limits.
- Portable build script and Windows CI.

### Changed
- Server defaults to loopback-only.
- Pairing links are invalidated after approval; revocation cancels active device requests.
- Documentation is English-first; original Czech planning is archived.

### Known gaps
- HTTP is unencrypted; physical iPhone and firewall tests are pending.
- Resume, secure updates, installer and a complete Core/Transport split are future work.

## 0.1.0 — 2026-10-11

- Initial C# server and Czech browser prototype with QR approval and bidirectional streaming.
