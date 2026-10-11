# LAN Drop

**Send files between Windows and your phone over a local network.**

LAN Drop is an early development preview built with C# / .NET 10. The Windows app generates a QR code; the phone connects in Safari without an account or a native iOS app. Files stay on your devices.

**This preview uses unencrypted HTTP. Use a trusted private network only. It is not a stable or security-hardened release.** Publishing the source does not mean the public-release requirements in the product design are complete.

## Current features

- Native Windows WPF window, network interface selection and explicit start/stop sharing.
- Locally generated QR code, approval on the PC, short-lived pairing and revocable sessions.
- Per-file incoming offers: check the phone, name and size on the PC, then accept or reject before upload.
- Individual recipients: each phone can list/download only files addressed to it.
- Multiple files uploaded sequentially, progress, speed, time estimates and cancellation; empty files supported.
- SHA-256 preparation, transfer checks and stored-file readback before confirming a saved PC copy.
- Streaming to disk with duplicate-name protection, Windows filename checks and incomplete-upload cleanup.
- Destination folder dialog, persistent folder/network/language preferences and notification-area access.
- English and Czech on Windows and the web; automatic language option and English fallback.
- Default loopback-only server; optional single selected IPv4 interface for phone access.
- Upload concurrency and per-run byte/file limits.
- Desktop network diagnostics, a start-time private/domain profile gate and periodic monitoring that stops unsafe sharing.
- Free-space checks, concurrent disk reservations and path checks that reject reparse points.

## Run from source

Requirements: **Windows and .NET 10 SDK**. Windows 11 x64 is the initial target; physical iPhone testing is still pending.

```powershell
dotnet build LanDrop.slnx -c Release
dotnet run --project src/LanDrop.Desktop -c Release --no-build
```

Or double-click **Start-LAN-Drop.cmd**. It builds the solution and opens the Windows application.

1. Choose the Wi-Fi or Ethernet interface connected to the phone's network. Desktop sharing requires a private or domain profile; public/unknown profiles are blocked.
2. Click **Start sharing**, scan the QR on the phone and enter a device name.
3. Approve that request on the PC. Generate a new QR to connect another phone.
4. Choose a recipient before adding files on the PC. On the phone, use **Download**.
5. On the phone, choose files. Accept each request on the PC to start its upload to the chosen folder (default: Downloads/LAN-Drop). Reject it or cancel on the phone to skip sending. Requests expire after ten minutes.
6. **Hide to notification area** keeps sharing running; double-click the tray icon to reopen. Closing the window stops sharing.

Safari manages downloaded files; this app does not automatically save images into Photos or claim that a native browser download has been saved successfully. Keep the Safari tab open during uploads.

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

The output is `publish/LAN-Drop-<version>-win-x64/` (currently `LAN-Drop-0.2.0-alpha.4-win-x64`). Each version gets its own folder so building a new preview does not overwrite a running older preview. Run `LanDrop.Desktop.exe` and keep the bundled `Server` folder alongside it. The self-contained package does not require the SDK on the recipient PC. The package is currently unsigned; an installer, trusted updates and code signing are planned.

## Verification

```powershell
dotnet build LanDrop.slnx -c Release
python tests/integration.py
python tests/storage_safety.py
node tests/localization.cjs
node tests/progress.cjs
node tests/offers.cjs
node tests/integrity.cjs
dotnet run --project tests/NetworkPolicy -c Release
dotnet run --project tests/DesktopIntegrity -c Release
dotnet run --project src/LanDrop.Desktop -c Release --no-build -- --smoke-test
python tests/desktop_lifecycle.py
```

Tests cover two-device authorization, pairing, recipient isolation, receive approval/rejection/cancellation/replay, checksums and corrupt uploads, worker cancellation, receipt validation, transfers, cookies, ranges, filenames, interrupted uploads, storage safety, language resources, progress calculations, profile policy and real desktop upload/download. Tests use loopback and do not validate a real iPhone, Wi-Fi or firewall.

Browser hash assets are already bundled for normal source/portable use; no internet request is made during hashing. To regenerate them, use Node 24 and `npm ci --ignore-scripts`, then `npm run build:hash`. See [integrity protocol](docs/INTEGRITY.md) and [third-party notices](THIRD-PARTY-NOTICES.md).

## Known limitations

- HTTP is not encrypted. Trusted HTTPS in Safari remains an architecture decision and a stable-release gate.
- No resumable upload or persistent transfer history yet.
- Checksums verify the saved PC copy at commit time. They do not encrypt or authenticate HTTP, guarantee survival of power loss, or verify a Safari download. Later changes to saved files are not automatically detected.
- Hash preparation reads the source before upload, and the server reads the saved temporary copy again. Large-file performance and real iPhone behavior still need measurement.
- Incoming offers are per file, valid for ten minutes and bound to the device, name and declared size. They do not verify file contents. An attempted upload consumes approval even if it fails; retry with a new request.
- The 10 GB per-file limit is a configured ceiling, not a benchmarked guarantee.
- Byte quota is cumulative for the server run; deleting an outgoing share releases its bytes. Hiding incoming entries preserves their files and quota. Restart resets the quota; this is not total-disk accounting.
- Inactive owned outboxes are cleaned after a crash. Unmarked folders and completed inbox files are preserved; incomplete inbox files may remain.
- Path checks reject reparse points but are not atomic protection against a hostile local process. Other disk consumers may fill free space between checks.
- Desktop checks network profiles before starting and every fifteen seconds while sharing. A missing address or public/unknown profile stops the server; detection can be delayed and does not prove phone reachability. The browser-only server does not monitor profiles. Automatic firewall setup, secure updates and an installer are pending.
- Native client currently talks to a child server through authenticated loopback HTTP. The future Core/Transport split is not yet complete.

See [product design](PRODUCT-DESIGN.md), [roadmap](ToDo.md), [changes](CHANGELOG.md) and [security policy](SECURITY.md). [Archived Czech design](docs/cs/PRODUCT-DESIGN.md) records the original proposal and may describe older behavior.
