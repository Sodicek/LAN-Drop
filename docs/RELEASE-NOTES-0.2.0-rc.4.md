# LAN Drop 0.2.0-rc.4

This candidate adds recovery for incomplete incoming uploads after a server process crash. New incoming transfers use an owned staging directory in the selected destination. A complete length and matching SHA-256/stored-file readback are still required before a same-volume, non-overwriting move publishes a completed file.

Startup and receive-folder selection discard only known inactive staging with validated ownership and an available exclusive lease. Live runs, unrelated files, legacy loose `.part` files, malformed markers, unexpected entries and unsafe paths are preserved. Completed received files remain untouched. Changing the destination keeps active uploads bound to their original folder.

The Windows app displays recovery counts or a notice when staging was preserved. Diagnostic JSON schema 2 adds only bounded recovery counts; names, paths, addresses, filenames, passwords, tokens, contents and logs remain excluded. Read [recovery](RECOVERY.md) ([česky](cs/RECOVERY.md)) and [diagnostics](DIAGNOSTICS.md) ([česky](cs/DIAGNOSTICS.md)).

## Verification and limits

Automated recovery checks use real isolated server processes, partial uploads, forced termination and restart. They verify completed-file preservation, live-run isolation, refused unknown/unsafe staging and folder-switch behavior. Existing HTTPS, authorization, integrity, storage, native lifecycle and Chromium/WebKit mobile-profile checks remain in CI.

Cleanup is best-effort, bounded and applies only to selected receive folders. Historical untracked partial files are left untouched. Old phone sessions and approvals are lost on restart; unfinished files require fresh consent and a new transfer. Check for an already saved file before retrying after an ambiguous response. Queues/activity remain current-run only and transfers are not resumable.

This remains a pre-release: physical Android/iPhone trust setup and actual HTTPS, QR/file-picker behavior, Wi-Fi/firewall/background recovery, clean Windows and code signing are still pending. See [release matrix](RELEASE-TEST-MATRIX.md). Process-crash tests do not prove power-loss durability or protection against hostile local path replacement.

Extract the entire unsigned Windows x64 ZIP and run `LanDrop.Desktop.exe` with `Server` beside it. English and Czech are included. Set up certificate trust following [HTTPS setup](HTTPS.md) ([česky](cs/HTTPS.md)); passwords stay session-only. The SHA-256 asset checks downloaded bytes, not publisher identity.
