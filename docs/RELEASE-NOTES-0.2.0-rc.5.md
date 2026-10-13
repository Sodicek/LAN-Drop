# LAN Drop 0.2.0-rc.5

This candidate adds **opt-in local completion history**, disabled by default. Enable it in the Windows app to retain up to 200 recent completed PC saves and outbound file preparations across restarts. Records include the actual completed filename, size, direction and time, with no paths, device names, credentials or file contents.

Saved history can be read before sharing starts. Disabling recording keeps earlier records; clearing history removes only metadata and leaves files and live access unchanged. An outgoing record confirms preparation for a phone, not its download. Restart restores neither phone sessions nor queues or grants. See [history](HISTORY.md) ([česky](cs/HISTORY.md)).

The bounded store uses an atomic JSON checkpoint and an exclusive writer lease. Unknown, corrupted or redirected storage is preserved and reported as unavailable. History failures do not turn a successfully committed file into a failed transfer response. Recording is best-effort; a crash between file completion and metadata commit can leave a file without a row. Inspect the receive folder before retrying an ambiguous transfer.

Custom app-data and receive paths that overlap the configured public web root are rejected before private files are created there, keeping local metadata separate from public static assets.

## Verification and limits

Automated history checks exercise opt-in/default-off behavior, completed-only recording, restart and session isolation, metadata-only clearing, disabled retention, bounds, concurrent writers and corrupt/unsafe storage. Existing crash recovery, HTTPS, authorization, mobile-profile and native lifecycle checks remain in CI.

History filenames are local personal metadata; the [anonymous diagnostic export](DIAGNOSTICS.md) does not include them. Queues and active transfer activity remain current-run only; uploads are not resumable. Physical Android/iPhone trust and HTTPS, real network/firewall/background behavior, clean Windows and code signing are still pending. See [release matrix](RELEASE-TEST-MATRIX.md).

Extract the whole unsigned Windows x64 ZIP and run `LanDrop.Desktop.exe` with its `Server` folder beside it. English/Czech interfaces and [HTTPS guides](HTTPS.md) ([česky](cs/HTTPS.md)) are included. The checksum asset verifies downloaded bytes, not publisher identity.
