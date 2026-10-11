# LAN Drop 0.2.0-rc.1

This release candidate groups incoming files into one approval request, shows transfer states, and supports retrying unfinished selections without selecting the files again. Phone-upload retries require fresh Windows consent. Confirmed completed files are not included in that retry.

The application and documentation use English, with English and Czech interface translations. Android Chrome and iPhone Safari connect through a QR code without installing a phone application.

Security controls include device approval and revocation, per-recipient outbound files, SHA-256 saved-file receipts, bounded transfer/storage metadata, safe local storage checks, and Windows network-profile checks. Network transport remains **unencrypted HTTP**.

## Verification scope

Automated checks cover real local HTTP requests and Windows application lifecycle. The mobile browser test uses Chromium with Android emulation and iPhone user-agent defaults. It does not verify either physical Android hardware or Safari.

Physical Android, physical iPhone, and a clean Windows portable-package run are still required before declaring 0.2.0 stable. Firewall and sleep/wake behavior also need practical checks. See [the release test matrix](RELEASE-TEST-MATRIX.md) for the procedure and current verification boundaries.

Authenticated secure transport is also a stable-release gate. This candidate is for trusted private networks and is not a security-hardened stable release. Queues and activity are kept only for the current app/tab/server run. If a success response is lost, inspect the PC list before retrying to avoid duplicates.

## Run the portable preview

Download the Windows x64 ZIP and extract the entire folder. Close any older LAN Drop window, then run `LanDrop.Desktop.exe`; keep the bundled `Server` directory beside it. No SDK installation is required. The package is unsigned. The `.sha256` asset provides a ZIP checksum, not a signed publisher identity.
