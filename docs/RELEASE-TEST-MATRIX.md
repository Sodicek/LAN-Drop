# LAN Drop 0.2.0 release test matrix

0.2.0 remains a release candidate until the physical-device, secure phone setup and clean-Windows checks below are recorded. HTTPS is implemented in rc.2 with strict automated TLS validation. A successful browser emulation run does not verify Android Chrome on hardware or iPhone Safari. The [expanded mobile simulation report](MOBILE-SIMULATION.md) records Chromium and WebKit results, including the corrected rc.1 Unicode download-name limitation. The repository's Actions run records the complete CI result for the corresponding commit.

| Check | Environment | Evidence / status |
| --- | --- | --- |
| Pairing, permissions, checksums, recipient isolation, quota and replay | Real local HTTP server | Automated: `python tests/integration.py` |
| Grouped consent, twenty files, atomic validation, retry and receipt isolation | Real local HTTP server | Automated: `python tests/batches.py` |
| Grouped selection, cancel, offline retry without reselection, download, rotation and revocation | Chromium with Pixel 7 and Windows WebKit with iPhone 13 profiles | Passed: `node tests/mobile_browser.cjs`, including original Unicode Download-action names; not physical Android/iOS |
| Validated TLS, untrusted root and hostname rejection, no HTTP fallback, secure cookies, transfer integrity and revoke | Python/OpenSSL TLS client with explicit isolated CA trust | Passed: `python tests/secure_transport.py`; no Windows root installation or validation bypass |
| Local certificate generation, validity/private key/EKU/SAN checks and rollback | Windows development host | Passed: `dotnet run --project tests/Transport -c Release` |
| Certificate inspection, actionable status and verified companion authority | Isolated local certificate fixtures | Automated: `tests/Transport`; no network downloads or trust changes |
| Manual diagnostic summary privacy | Typed report helper and native window | Automated: `tests/Diagnostics` and desktop lifecycle; explicit field whitelist |
| Chromium phone profile over trusted HTTPS | Disposable Windows GitHub Actions runner | Automated: `tests/Run-TrustedBrowser.ps1`; CI-only test root is removed afterward; no local trust mutation |
| Native window and child-server startup/shutdown | Windows development host and CI | Automated desktop smoke and lifecycle checks |
| Self-contained portable package lifecycle | Windows development host with SDK/runtime discovery removed from PATH and DOTNET_ROOT | Passed: `python tests/desktop_lifecycle.py --executable publish/LAN-Drop-0.2.0-rc.4-win-x64/LanDrop.Desktop.exe --without-sdk`, including HTTPS preflight, expiry shutdown and private diagnostics; does not replace a clean-machine test |
| Safe storage paths, disk shortage, stale outbox ownership | Windows development host and CI | Automated: `python tests/storage_safety.py` |
| Incoming partial upload, forced process termination/restart, preserved completed files and live leases | Real isolated Windows server processes | Passed: `python tests/incoming_recovery.py`; includes folder switching, same-folder retry and malformed/foreign/nested/linked/locked preservation; not power-loss testing |
| Android Chrome on a real phone | Private LAN and Windows PC | **Not run; release gate** |
| iPhone Safari on a real phone | Private LAN and Windows PC | **Not run; release gate** |
| Portable package without installed .NET or developer tools | Clean supported Windows machine | **Not run; release gate** |
| Firewall denies inbound traffic; correct help and recovery | Clean Windows and physical phone | **Not run** |
| PC sleep/wake, Wi-Fi switch, phone background/foreground | Physical devices | **Not run** |
| Phone authority installation, trusted HTTPS and renewal | Physical Android Chrome and iPhone Safari | **Not run; release gate** |

## Physical-device acceptance procedure

Record the Windows version, app version, phone model, OS/browser versions, transport mode and network setup, with the date and tester. Use non-sensitive test data while validating this candidate.

1. Set up and verify certificate trust following [HTTPS setup](HTTPS.md). Start the portable app on a private LAN in HTTPS mode. Scan its secure QR code, approve the phone, and confirm the correct device name.
2. Select twenty photos. Confirm that Windows displays all names and total size in a single request. Verify that nothing is saved before acceptance; accept once, compare all saved files, and confirm completed states.
3. Repeat with rejection and phone cancellation. Verify that no completed or partial file remains.
4. Interrupt a transfer by disconnecting Wi-Fi. Restore connectivity and retry without reselecting the file. Confirm fresh consent, exact saved bytes, and no duplicate of a file already completed.
5. Send Windows files back to the phone, including an empty file and a Unicode filename. Verify saved names and bytes using the browser's normal download flow.
6. Block the server in Windows Firewall, then restore access. Record the message and recovery. Do not change the firewall automatically.
7. Sleep/wake Windows, background/restore the phone browser, and switch the PC network to Public. Confirm safe stop and a clear reconnect path.
8. Repeat EN and CS, revoke the device, and verify old session/download access fails.
9. Reject an untrusted authority, an expired certificate and a changed IP address. Confirm that no certificate-warning bypass or HTTP fallback is needed. Replace the identity and its specific phone trust, then repeat a transfer.
10. Interrupt an upload by terminating its isolated test application, restart and inspect [recovery](RECOVERY.md). Verify completed files are preserved and only owned inactive staging is discarded. Check for a completed file before retrying if the confirmation was lost.

Fill in actual results before promoting a candidate to a stable release; do not mark an unperformed check as passed.
