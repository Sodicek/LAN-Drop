# LAN Drop 0.2.0 release test matrix

0.2.0 is a release candidate until the physical-device and clean-Windows checks below are recorded and authenticated secure transport is implemented and verified. A successful browser emulation run does not verify Android Chrome on hardware or iPhone Safari. New grouped-consent, retained-selection, mobile-emulation and native-lifecycle checks passed on the Windows development host on 2026-10-11. The repository's Actions run records the complete CI result for the corresponding commit.

| Check | Environment | Evidence / status |
| --- | --- | --- |
| Pairing, permissions, checksums, recipient isolation, quota and replay | Real local HTTP server | Automated: `python tests/integration.py` |
| Grouped consent, twenty files, atomic validation, retry and receipt isolation | Real local HTTP server | Automated: `python tests/batches.py` |
| Grouped selection, cancel, failed-upload retry without reselection, download | Chromium with Pixel 7 emulation | Automated: `node tests/mobile_browser.cjs`; not physical Android |
| Native window and child-server startup/shutdown | Windows development host and CI | Automated desktop smoke and lifecycle checks |
| Self-contained portable package lifecycle | Windows development host with SDK/runtime discovery removed from PATH and DOTNET_ROOT | Passed: `python tests/desktop_lifecycle.py --executable publish/LAN-Drop-0.2.0-rc.1-win-x64/LanDrop.Desktop.exe --without-sdk`; does not replace a clean-machine test |
| Safe storage paths, disk shortage, stale outbox ownership | Windows development host and CI | Automated: `python tests/storage_safety.py` |
| Android Chrome on a real phone | Private LAN and Windows PC | **Not run; release gate** |
| iPhone Safari on a real phone | Private LAN and Windows PC | **Not run; release gate** |
| Portable package without installed .NET or developer tools | Clean supported Windows machine | **Not run; release gate** |
| Firewall denies inbound traffic; correct help and recovery | Clean Windows and physical phone | **Not run** |
| PC sleep/wake, Wi-Fi switch, phone background/foreground | Physical devices | **Not run** |
| HTTPS / confidential transport | Physical browsers and server | **Not implemented; HTTP only** |

## Physical-device acceptance procedure

Record the Windows version, app version, phone model, OS/browser versions and network setup, with the date and tester. Use non-sensitive test data because transport currently uses HTTP.

1. Start the portable app on a private LAN. Scan its QR code, approve the phone, and confirm the correct device name.
2. Select twenty photos. Confirm that Windows displays all names and total size in a single request. Verify that nothing is saved before acceptance; accept once, compare all saved files, and confirm completed states.
3. Repeat with rejection and phone cancellation. Verify that no completed or partial file remains.
4. Interrupt a transfer by disconnecting Wi-Fi. Restore connectivity and retry without reselecting the file. Confirm fresh consent, exact saved bytes, and no duplicate of a file already completed.
5. Send Windows files back to the phone, including an empty file and a Unicode filename. Verify saved names and bytes using the browser's normal download flow.
6. Block the server in Windows Firewall, then restore access. Record the message and recovery. Do not change the firewall automatically.
7. Sleep/wake Windows, background/restore the phone browser, and switch the PC network to Public. Confirm safe stop and a clear reconnect path.
8. Repeat EN and CS, revoke the device, and verify old session/download access fails.

Fill in actual results before promoting a candidate to a stable release; do not mark an unperformed check as passed.
