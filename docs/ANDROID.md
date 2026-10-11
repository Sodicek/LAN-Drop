# Android preview

LAN Drop uses a browser client on Android. There is no APK to install. Chrome on Android is the initial target; other browsers remain unverified. The protocol is the same as the iPhone client, including device approval, grouped file acceptance and checksum verification of the saved PC copy.

1. Connect Windows and Android to a trusted reachable local network. Start sharing in LAN Drop on Windows using its private/domain network interface.
2. Scan the QR with the phone's camera or QR scanner. If supported, Google Lens can also scan it. Open the link in Chrome. Available camera/scanner controls vary by device; see [Google's Android QR guide](https://www.android.com/intl/en_uk/articles/how-do-you-scan-qr-codes-on-android/).
3. Check the detected phone type, or select Android manually. Name the phone and approve the device on the PC.
4. Choose files on Android. The operating system's picker may offer photos, videos or documents. Accept the single request showing file names and total size on the PC. The phone then prepares checksums and sends each file. Leave the browser visible and the screen awake until the PC confirms saving.
5. To receive from Windows, choose this phone as the recipient on the PC and prepare files. Tap Download on Android, then check Chrome's Downloads. The browser manages the save flow; see [Chrome's Android download guide](https://support.google.com/chrome/answer/95759?co=GENIE.Platform%3DAndroid&hl=en).

If the picker or downloads fail in a QR scanner's embedded browser, open the pairing link in Chrome instead. A capability message explains when the current browser cannot upload. Picking Android manually changes instructions and the suggested device name; it does not override capability checks or permissions.

Uploads may be interrupted by screen locking, app switching, sleep or network changes. Keep the tab open and use Retry unfinished files after connectivity returns; Windows must accept a fresh request. Files with confirmed completion are excluded. Retry starts unfinished files from the beginning; resumable and guaranteed background transfer are not implemented. If the success response was lost, inspect the PC list before retrying. Clear selection releases unfinished files from the queue without deleting completed files. Do not bypass browser or organization security controls to transfer a blocked file.

## Verification status

Automated protocol and browser tests do not prove physical-device compatibility. Actual Android file pickers, QR-camera flow, gallery visibility, storage permissions, download notifications, background behavior and large-file performance must be checked on real hardware. Record Android version, phone model, Chrome version and network type when reporting an issue. Do not share pairing URLs, session tokens or personal files in public reports.

The Windows app defaults to HTTPS in rc.2. Establish explicit phone trust following [HTTPS setup](HTTPS.md) before scanning the secure QR. The optional HTTP preview remains unencrypted. Checksums check accidental corruption on the PC; they do not independently verify Android's downloaded copy or replace TLS validation.
