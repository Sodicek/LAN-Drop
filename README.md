# LAN Drop

**Send files between Windows and your phone over a local network.**

LAN Drop is an early development preview built with C# / .NET 10. The Windows app generates a QR code; the phone connects in Safari without an account or a native iOS app. Files stay on your devices.

**This preview uses unencrypted HTTP. Use a trusted private network only. It is not a stable or security-hardened release.** Publishing the source does not mean the public-release requirements in the product design are complete.

## Current features

- Native Windows WPF window, network interface selection and explicit start/stop sharing.
- Locally generated QR code, approval on the PC, short-lived pairing and revocable sessions.
- Individual recipients: each phone can list/download only files addressed to it.
- Multiple files uploaded sequentially, progress and cancellation; empty files supported.
- Streaming to disk with duplicate-name protection, Windows filename checks and incomplete-upload cleanup.
- Destination folder dialog, persistent folder/network/language preferences and notification-area access.
- English and Czech on Windows and the web; automatic language option and English fallback.
- Default loopback-only server; optional single selected IPv4 interface for phone access.
- Upload concurrency and per-run byte/file limits.

## Run from source

Requirements: **Windows and .NET 10 SDK**. Windows 11 x64 is the initial target; physical iPhone testing is still pending.

```powershell
dotnet build LanDrop.slnx -c Release
dotnet run --project src/LanDrop.Desktop -c Release --no-build
```

Or double-click **Start-LAN-Drop.cmd**. It builds the solution and opens the Windows application.

1. Choose the Wi-Fi or Ethernet interface connected to the phone's network.
2. Click **Start sharing**, scan the QR on the phone and enter a device name.
3. Approve that request on the PC. Generate a new QR to connect another phone.
4. Choose a recipient before adding files on the PC. On the phone, use **Download**.
5. Phone uploads are saved to the chosen folder (default: Downloads/LAN-Drop).
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

Replace the example address with your PC's address. Administration remains authenticated and loopback-only. The startup URL contains a temporary administrator secret; do not share it or publish console output.

Options: `--Port=45821`, `--OpenBrowser=false`, `--DataDirectory=<local-path>`, `--MaxConcurrentUploads=3`, `--MaxStoredBytes=21474836480`, `--MaxRegisteredFiles=1000`.

## Languages

English is the default. Choose **Čeština** or **Automatic** independently on the PC and phone. Settings persist locally. Additional languages can be contributed; see [localization guide](docs/LOCALIZATION.md).

## Build a portable preview

```powershell
./scripts/Publish-Portable.ps1
```

The output is `publish/LAN-Drop-win-x64/`. Run `LanDrop.Desktop.exe` and keep the bundled `Server` folder alongside it. The self-contained package does not require the SDK on the recipient PC. The package is currently unsigned; an installer, trusted updates and code signing are planned.

## Verification

```powershell
dotnet build LanDrop.slnx -c Release
python tests/integration.py
node tests/localization.cjs
dotnet run --project src/LanDrop.Desktop -c Release --no-build -- --smoke-test
```

Tests cover two-device authorization, pairing, recipient isolation, upload/download, cookies, ranges, filenames, duplicates, empty files, interrupted uploads and language resources. Tests use loopback and do not validate a real iPhone, Wi-Fi or firewall.

## Known limitations

- HTTP is not encrypted. Trusted HTTPS in Safari remains an architecture decision and a stable-release gate.
- No resumable upload, checksum verification, per-file receive approval or persistent transfer history yet.
- Every approved phone may upload to the configured inbox; outgoing files are isolated by recipient.
- The 10 GB per-file limit is a configured ceiling, not a benchmarked guarantee.
- Byte quota is cumulative for the server run; deleting an outgoing share releases its bytes. Hiding incoming entries preserves their files and quota. Restart resets the quota; this is not total-disk accounting.
- A crash may leave temporary files/copies. Automatic crash cleanup and reparse-point protection need additional work.
- Adapter/profile diagnostics, automatic firewall setup, secure updates and an installer are pending.
- Native client currently talks to a child server through authenticated loopback HTTP. The future Core/Transport split is not yet complete.

See [product design](PRODUCT-DESIGN.md), [roadmap](ToDo.md), [changes](CHANGELOG.md) and [security policy](SECURITY.md). [Archived Czech design](docs/cs/PRODUCT-DESIGN.md) records the original proposal and may describe older behavior.
