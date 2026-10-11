# Changelog

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
