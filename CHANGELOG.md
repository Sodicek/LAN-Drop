# Changelog

## 0.2.0-rc.6 — 2026-10-13

### Added
- Independent persisted System/Light/Dark appearance on Windows and in the browser, with English/Czech names and live System updates.
- Semantic light/dark palettes, visible keyboard focus and system high-contrast/forced-colors handling.
- Localized native automation names, contextual repeated actions and browser progress names/values.
- Keyboard focus retention and expanded browser batch details across polling updates.
- Existing completion notices/history labels update when switching browser language.
- Native/browser theme, selected contrast, localization, persistence and focus checks with CI screenshots.

### Known gaps
- Actual screen readers, phone assistive technology, custom Windows contrast themes, large text/DPI and full keyboard/accessibility audits remain unverified.
- Physical phones, clean Windows and signing still gate stable distribution; themes do not change transport or transfer limits.

## 0.2.0-rc.5 — 2026-10-13

### Added
- Opt-in local completion history, disabled by default, retaining up to 200 filenames, sizes, directions and timestamps across restarts.
- Read-only history before sharing starts, separate from current-run activity and download permissions.
- Disable-with-retention and metadata-only clearing that preserves files and live access.
- Bounded validated history checkpoints, exclusive writer lease and history-only failure handling.
- Reject app-data and receive destinations that overlap the configured public web root, before creating private storage there.
- English/Czech history controls and real restart, privacy, clear, concurrency, corruption and 201-completion limit checks.

### Known gaps
- Outgoing history proves PC preparation, not phone download/storage. A crash between file commit and history commit may leave a completed file without a history row.
- Queues, sessions and approvals remain current-run only; transfers are not resumable.
- Physical phone/clean Windows verification, code signing and power-loss durability remain pending.

## 0.2.0-rc.4 — 2026-10-13

### Added
- Owned incoming staging with exclusive run leases and best-effort cleanup after a process crash.
- Recovery during startup and receive-folder selection, preserving completed files, live runs, legacy partial files and unknown/unsafe staging.
- Folder switching that retains each active upload's original destination.
- English/Czech recovery notices and anonymous bounded recovery counters in diagnostic schema 2.
- Real partial-upload/crash/restart tests and live-run, ownership and unsafe-entry preservation checks.

### Known gaps
- Cleanup covers selected folders and a bounded number of candidates; it does not search every historical destination or resume discarded uploads.
- A crash after a completed move but before a response can leave a complete file without sender confirmation; inspect the folder before retrying.
- Hostile local path replacement, power-loss durability, physical phone/network recovery and clean Windows remain unverified.

## 0.2.0-rc.3 — 2026-10-13

### Added
- Certificate preflight with EN/CS explanations for missing input, invalid password/file, validity, IP and server-usage failures.
- Certificate IP/expiry summary, seven-day renewal warning and periodic stop on active certificate expiry.
- Verified companion public authority details after restarting and checking a certificate; no automatic phone trust installation.
- Manual local diagnostic JSON export with a strict whitelist of versions, categories, check status and counts; no addresses, names, paths, files, fingerprints, credentials or logs.
- Certificate inspection/companion-chain tests, diagnostic privacy checks and expanded native lifecycle proofs.

### Known gaps
- Physical phone certificate installation and real Android/iPhone HTTPS remain release gates.
- Local preflight does not verify phone trust or network reachability; periodic monitoring can be delayed.
- Distribution remains unsigned and clean-machine/firewall/background checks remain pending.

## 0.2.0-rc.2 — 2026-10-11

### Added
- HTTPS phone listener with TLS 1.2/1.3, authenticated loopback-only administration and no automatic HTTP fallback.
- Desktop HTTPS default, PFX selection, thirty-day local certificate generation, public CA fingerprint copying and English/Czech trust setup help.
- Encrypted server PFX export, validation of private key/validity/server usage/IP names, and session-only passwords passed outside process arguments.
- HTTPS QR links, secure download cookies and clear browser/native connection status.
- Strict real TLS tests for trust, hostname mismatch, invalid configuration, transfers, ranges and revocation; expanded Chromium/WebKit mobile simulation.

### Fixed
- The phone Download action now preserves Unicode file names in Windows WebKit by removing the conflicting ASCII disposition fallback for those names.
- Windows Schannel key-loading compatibility and certificate authority-key identifiers found by strict TLS tests.
- Partial certificate outputs are rolled back without overwriting existing files.

### Verification boundaries
- Phone authority installation, physical Android/iPhone HTTPS, clean Windows and network/background checks remain unperformed.
- Generated identities expire after thirty days or need replacement when the selected IP changes; phone trust must be set up again. No automatic root installation or renewal.
- HTTP preview is explicitly available and unencrypted. Distribution remains unsigned.

## 0.2.0-rc.1 — 2026-10-11

Release candidate; physical-device, clean-machine and secure-transport gates remain open.

### Added
- Batch receive offers with names, total size and one PC decision for the complete selection.
- Current-session transfer activity with progress, verification, completed, failed and cancelled states.
- Manual retry of unfinished browser/native selections without reopening the file picker; verified completed items are skipped.
- English/Czech connection help for private networks, guest isolation, firewall access, changed addresses and sleep recovery.
- Batch/queue/retry tests, expanded native lifecycle checks and physical release-test procedures.
- Versioned portable ZIP packaging with SHA-256 checksums and included documentation.

### Known gaps
- HTTP is unencrypted; authenticated secure transport is required before a security-hardened stable release.
- Physical Android/iPhone, clean Windows, managed firewall and sleep/wake checks are not yet performed.
- Queues and transfer activity are in memory for the current tab/app/run. Retry starts again and requires fresh consent; it is not resume.
- An ambiguous lost success response can leave a saved file; check the PC transfer list before retrying.

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
