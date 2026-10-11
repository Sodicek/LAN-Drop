# Android preview

LAN Drop uses a browser client on Android. There is no APK to install. Chrome on Android is the initial target; other browsers remain unverified. The protocol is the same as the iPhone client, including device approval, per-file acceptance and checksum verification of the saved PC copy.

1. Connect Windows and Android to a trusted reachable local network. Start sharing in LAN Drop on Windows using its private/domain network interface.
2. Scan the QR with the phone's camera or QR scanner. If supported, Google Lens can also scan it. Open the link in Chrome. Available camera/scanner controls vary by device; see [Google's Android QR guide](https://www.android.com/intl/en_uk/articles/how-do-you-scan-qr-codes-on-android/).
3. Check the detected phone type, or select Android manually. Name the phone and approve the device on the PC.
4. Choose files on Android. The operating system's picker may offer photos, videos or documents. Wait for checksum preparation, then accept each file request on the PC. Leave the browser visible and the screen awake until the PC confirms saving.
5. To receive from Windows, choose this phone as the recipient on the PC and prepare files. Tap Download on Android, then check Chrome's Downloads. The browser manages the save flow; see [Chrome's Android download guide](https://support.google.com/chrome/answer/95759?co=GENIE.Platform%3DAndroid&hl=en).

If the picker or downloads fail in a QR scanner's embedded browser, open the pairing link in Chrome instead. A capability message explains when the current browser cannot upload. Picking Android manually changes instructions and the suggested device name; it does not override capability checks or permissions.

Uploads may be interrupted by screen locking, app switching, sleep or network changes. Cancel/reselect the file and obtain a new acceptance on the PC. Resume and guaranteed background transfer are not implemented. Do not bypass browser or organization security controls to transfer a blocked file.

## Verification status

Automated protocol and browser tests do not prove physical-device compatibility. Actual Android file pickers, QR-camera flow, gallery visibility, storage permissions, download notifications, background behavior and large-file performance must be checked on real hardware. Record Android version, phone model, Chrome version and network type when reporting an issue. Do not share pairing URLs, session tokens or personal files in public reports.

HTTP remains unencrypted. Checksums check accidental corruption on the PC; they do not independently verify Android's downloaded copy or provide encryption.
