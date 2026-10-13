# LAN Drop 0.2.0-rc.6

This candidate adds **System / Light / Dark** appearance on Windows and in the phone browser, with English/Czech controls and independent local preferences. System is the default and follows appearance changes; high contrast/forced colors and reduced motion remain respected.

Keyboard focus is more visible and survives periodic replacement of dynamic actions that still exist. Repeated approval, removal and download actions have contextual accessible names. Windows control names and browser progress labels/values follow the selected language. Browser batch details remain open during polling. See [appearance and keyboard access](APPEARANCE-ACCESSIBILITY.md) ([česky](cs/APPEARANCE-ACCESSIBILITY.md)).

## Verification and limits

Automated checks cover preference storage/fallback, System/manual updates, tested normal text/control contrast, localized names, focus retention, reduced motion, Chromium forced-colors emulation and native palette/high-contrast mapping. Mobile profiles and native screenshots accompany the existing HTTPS, authorization, integrity, history and crash-recovery suite.

This is not a full accessibility audit or certification. Actual screen readers, phone assistive technology, user-defined Windows contrast modes, large text/DPI and complete keyboard workflows remain unverified. Physical Android/iPhone trust setup and HTTPS, real Wi-Fi/firewall/background behavior, clean Windows and code signing also remain pending. See [release matrix](RELEASE-TEST-MATRIX.md).

Extract the whole unsigned Windows x64 ZIP and run `LanDrop.Desktop.exe` with `Server` beside it. EN/CS interfaces and [HTTPS setup](HTTPS.md) ([česky](cs/HTTPS.md)) are included. History remains optional/off by default; queues are current-run only and uploads are not resumable. The checksum verifies downloaded bytes, not publisher identity.
