# LAN Drop 0.2.0-rc.2

This candidate adds HTTPS phone connections and corrects Unicode names in the phone's Download action. The Windows app defaults to secure mode, supports selecting or generating a local certificate, and keeps its password only in the current session. Administration stays on authenticated loopback HTTP; secure mode has no plaintext LAN fallback.

Generated identities expire after thirty days and cover the selected IPv4 address. The public authority must be explicitly trusted on the phone. The app shows its SHA-256 fingerprint and never installs a root automatically. Send only the public `.ca.cer` to the phone; keep the encrypted `.pfx` and password on the PC. Read [HTTPS setup](HTTPS.md) ([Czech guide](cs/HTTPS.md)) before starting. Address changes and renewal require replacement and renewed phone trust.

## Verification

Automated checks exercise strict TLS trust and hostname validation, secure cookies, upload/download integrity, ranges and revocation. Chromium and WebKit mobile profiles exercise real workers, grouped consent for twenty files, cancellation, offline retry without duplicating confirmed files, empty/12 MiB binaries and exact Unicode download names. The disposable Windows CI runner additionally tests Chromium over trusted HTTPS without disabling certificate validation.

Physical Android/iPhone authority installation, Safari/Android HTTPS, QR cameras, native file pickers, background behavior, real Wi-Fi/firewall recovery and a clean Windows portable run remain unverified. See the [release matrix](RELEASE-TEST-MATRIX.md) and [mobile simulation report](MOBILE-SIMULATION.md). This is a pre-release; those checks still gate stable 0.2.0.

## Run

Extract the complete Windows x64 ZIP, close older LAN Drop windows and run `LanDrop.Desktop.exe`. Keep the `Server` folder beside it. No SDK installation is needed. The package is unsigned; the ZIP's SHA-256 asset checks downloaded bytes but does not authenticate a publisher.

English and Czech interfaces are included. HTTP preview remains an explicit, visibly unencrypted option for trusted private-network testing. Queues/activity remain current-run only, retries restart unfinished files, and phone browser downloads do not prove final phone storage. Windows temporarily imports the leaf key into its cryptographic provider for TLS; ordinary shutdown releases it, while forced process termination can leave provider artifacts.
