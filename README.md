# LAN Drop

**Send files between Windows and your phone over a local network.**

LAN Drop is an early development preview built with C# / .NET 10. The Windows app generates a QR code; Android connects in Chrome and iPhone connects in Safari, without an account or an installed phone app. Files stay on your devices. Physical-device compatibility checks are still pending.

**The Windows app now defaults to HTTPS; the phone must explicitly trust your certificate authority.** See [HTTPS setup](docs/HTTPS.md) ([česky](docs/cs/HTTPS.md)). An explicit unencrypted HTTP preview remains available. Physical-device and clean-machine verification are pending, so this is still a release candidate.

## Current features

- Native Windows WPF window, network interface selection and explicit start/stop sharing.
- HTTPS phone connections, local certificate generation or PFX selection, certificate preflight/expiry guidance, and session-only certificate passwords. Administration stays on authenticated loopback HTTP.
- Locally generated QR code, approval on the PC, short-lived pairing and revocable sessions.
- Incoming batch offers: check the phone, all names and total size on the PC, then accept or reject the selection before upload.
- Individual recipients: each phone can list/download only files addressed to it.
- Multiple files uploaded sequentially, progress, speed, time estimates and cancellation; empty files supported.
- SHA-256 preparation, transfer checks and stored-file readback before confirming a saved PC copy.
- Streaming to disk with duplicate-name protection, Windows filename checks and incomplete-upload cleanup.
- Destination folder dialog, persistent folder/network/language preferences and notification-area access.
- English and Czech on Windows and the web; automatic language option and English fallback.
- Android/iPhone guidance, automatic phone detection with a manual override, and upload capability checks.
- Current-session transfer states, manual retry of unfinished selections and connection-recovery help.
- Default loopback-only server; optional single selected IPv4 interface for phone access.
- Upload concurrency and per-run byte/file limits.
- Manual diagnostic export without addresses, names, paths or secrets; see [diagnostics](docs/DIAGNOSTICS.md) ([česky](docs/cs/DIAGNOSTICS.md)).
- Desktop network diagnostics, a start-time private/domain profile gate and periodic monitoring that stops unsafe sharing.
- Free-space checks, concurrent disk reservations and path checks that reject reparse points.

## Run from source

Requirements: **Windows and .NET 10 SDK**. Phone targets: current Chrome on Android and Safari on iPhone. Windows 11 x64 is the initial desktop target; physical phone testing is still pending.

```powershell
dotnet build LanDrop.slnx -c Release
dotnet run --project src/LanDrop.Desktop -c Release --no-build
```

Or double-click **Start-LAN-Drop.cmd**. It builds the solution and opens the Windows application.

1. Choose the Wi-Fi or Ethernet interface connected to the phone's network. Desktop sharing requires a private or domain profile; public/unknown profiles are blocked.
2. Keep **Secure HTTPS** enabled, create/select a certificate and establish its public authority's trust on the phone following the setup guide. Click **Start sharing**, scan the HTTPS QR and enter a device name. Certificate validation errors stop setup; do not bypass them.
3. Approve that request on the PC. Generate a new QR to connect another phone.
4. Choose a recipient before adding files on the PC. On the phone, use **Download**.
5. On the phone, choose files. Review the batch on the PC and accept once to upload them sequentially to the chosen folder (default: Downloads/LAN-Drop). Pending requests expire after ten minutes; batch acceptance is valid for two hours, bounded by the device session. Reject the batch or cancel on the phone to stop the selection.
6. **Hide to notification area** keeps sharing running; double-click the tray icon to reopen. Closing the window stops sharing.

The phone browser manages downloaded files. On Android, look in Chrome's Downloads; on iPhone, use Safari's downloads and Files. LAN Drop does not automatically save images into the gallery or claim that a browser download has been saved successfully. Keep the browser tab visible and the screen awake during preparation and uploads. If a QR scanner opens an embedded browser with a broken picker or download, open the link in Chrome on Android or Safari on iPhone. See the [Android guide](docs/ANDROID.md) ([česky](docs/cs/ANDROID.md)).

If the phone cannot connect, check that devices can reach each other and that Windows allows the selected application/port on the private network. Guest Wi-Fi can isolate clients. The app does not create firewall rules, alter the network profile or open router ports.

## Browser-only server

For local testing (no phone access):

```powershell
dotnet run --project src/LanDrop -c Release --no-launch-profile
```

For a phone, explicitly bind your own local IPv4 address:

```powershell
dotnet run --project src/LanDrop -c Release --no-launch-profile -- --ListenAddress=192.168.1.20
```

Replace the example address with your PC's address. The browser-only server does not enforce Windows network profiles; choose a trusted private network yourself. Administration remains authenticated and loopback-only. The startup URL contains a temporary administrator secret; do not share it or publish console output.

Options: `--Port=45821`, `--OpenBrowser=false`, `--DataDirectory=<local-path>`, `--MaxConcurrentUploads=3`, `--MaxStoredBytes=21474836480`, `--MaxRegisteredFiles=1000`, `--DiskSafetyBufferBytes=67108864`.

## Languages

English is the default. Choose **Čeština** or **Automatic** independently on the PC and phone. Settings persist locally. Additional languages can be contributed; see [localization guide](docs/LOCALIZATION.md).

## Build a portable preview

```powershell
./scripts/Publish-Portable.ps1
```

The output is `publish/LAN-Drop-<version>-win-x64/` (currently `LAN-Drop-0.2.0-rc.3-win-x64`). Each version gets its own folder so building a new preview does not overwrite a running older preview. Run `LanDrop.Desktop.exe` and keep the bundled `Server` folder alongside it. The self-contained package does not require the SDK on the recipient PC. The package is currently unsigned; an installer, trusted updates and code signing are planned.

Run `./scripts/Package-Release.ps1` after publishing to create a ZIP and its SHA-256 checksum. Candidate release notes and the [release test matrix](docs/RELEASE-TEST-MATRIX.md) explain which checks are automated and which still require real phones and clean Windows.

## Verification

```powershell
dotnet build LanDrop.slnx -c Release
python tests/integration.py
python tests/batches.py
python tests/storage_safety.py
dotnet run --project tests/Transport -c Release
dotnet run --project tests/Diagnostics -c Release
python tests/secure_transport.py
node tests/localization.cjs
node tests/progress.cjs
node tests/offers.cjs
node tests/integrity.cjs
node tests/mobile.cjs
node tests/queue.cjs
dotnet run --project tests/NetworkPolicy -c Release
dotnet run --project tests/DesktopIntegrity -c Release
dotnet run --project tests/DesktopQueue -c Release
dotnet run --project src/LanDrop.Desktop -c Release --no-build -- --smoke-test
python tests/desktop_lifecycle.py
```

Tests cover two-device authorization, pairing, recipient isolation, receive approval/rejection/cancellation/replay, checksums and corrupt uploads, worker cancellation, receipt validation, transfers, cookies, ranges, filenames, interrupted uploads, storage safety, language resources, progress calculations, profile policy and real desktop upload/download. Browser emulation is not a real Android/iPhone device test; loopback checks do not validate Wi-Fi, camera QR scanning or firewall behavior.

Browser hash assets are already bundled for normal source/portable use; no internet request is made during hashing. To regenerate them, use Node 24 and `npm ci --ignore-scripts`, then `npm run build:hash`. See [integrity protocol](docs/INTEGRITY.md) and [third-party notices](THIRD-PARTY-NOTICES.md).

For mobile browser checks, run `npm ci --ignore-scripts`, `npx playwright install chromium webkit`, then `node tests/mobile_browser.cjs` after building the server. This runs Chromium with a Pixel 7 profile and WebKit with an iPhone 13 profile against isolated local servers. Use `node tests/mobile_browser.cjs android` or `node tests/mobile_browser.cjs iphone` to select one profile. See [mobile simulation results and limitations](docs/MOBILE-SIMULATION.md). These checks exercise real workers, consent, saved-file receipts, offline retry and browser downloads; they do not exercise phone hardware or either operating system's file picker.

## Known limitations

- HTTPS is implemented with explicit certificate trust. Physical Android/iPhone authority installation, trusted-browser connections and renewal still need practical verification. HTTP preview traffic is unencrypted.
- No resumable upload or persistent transfer history yet. The current queue lives in the browser tab or Windows app; closing/reloading loses retained selections. Activity holds up to 200 entries for the current server run.
- Checksums verify the saved PC copy at commit time. They do not encrypt or authenticate HTTP, guarantee survival of power loss, or verify a phone browser download. Later changes to saved files are not automatically detected.
- Hash preparation reads the source before upload, and the server reads the saved temporary copy again. Large-file performance and real Android/iPhone behavior still need measurement.
- Incoming batch offers are bound to the device and each file's name/declared size. Pending offers expire after ten minutes and approved batches after two hours. An attempted upload consumes that file's grant even if it fails. Manual retry creates a new request for unfinished files; a lost success response may require checking the PC list to avoid duplicates.
- The 10 GB per-file limit is a configured ceiling, not a benchmarked guarantee.
- Byte quota is cumulative for the server run; deleting an outgoing share releases its bytes. Hiding incoming entries preserves their files and quota. Restart resets the quota; this is not total-disk accounting.
- Inactive owned outboxes are cleaned after a crash. Unmarked folders and completed inbox files are preserved; incomplete inbox files may remain.
- Path checks reject reparse points but are not atomic protection against a hostile local process. Other disk consumers may fill free space between checks.
- Desktop checks network profiles before starting and every fifteen seconds while sharing. A missing address or public/unknown profile stops the server; detection can be delayed and does not prove phone reachability. The browser-only server does not monitor profiles. Automatic firewall setup, secure updates and an installer are pending.
- Native client currently talks to a child server through authenticated loopback HTTP. The future Core/Transport split is not yet complete.

See [product design](PRODUCT-DESIGN.md), [roadmap](ToDo.md), [changes](CHANGELOG.md) and [security policy](SECURITY.md). [Archived Czech design](docs/cs/PRODUCT-DESIGN.md) records the original proposal and may describe older behavior.
