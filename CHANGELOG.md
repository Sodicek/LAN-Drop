# Changelog

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
