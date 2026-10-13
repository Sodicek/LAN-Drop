# Appearance and keyboard access

Windows **Appearance** and the browser **Theme** menu offer **System**, **Light** and **Dark**. System is the default for new or older settings. Choose Light or Dark to keep that appearance independently of the operating system. Preferences are local and independent on the PC and each browser.

Browser storage is scoped to the server address and port. A new QR origin after restarting sharing or changing the network can use a fresh browser preference. Windows remembers its own appearance independently of those addresses.

In System mode, Windows follows its application appearance preference and the browser follows `prefers-color-scheme`. A running client updates when the system preference changes. If browser storage is blocked, a selection still works for that tab but cannot be remembered after closing it. An unknown saved preference falls back to System.

Windows high contrast takes priority over manual colors. Native input fields and dropdowns retain matching Windows system foreground/background colors. Browser forced-colors mode uses system colors rather than disabling that mode; reduced-motion preferences suppress the waiting animation.

## Keyboard and accessible names

Use Tab and Shift+Tab to navigate enabled controls, arrow keys in selectors, and Enter/Space where supported by the native control or browser. Focus indicators are visible in both palettes. Dynamic approval, removal and download actions include their file or device context in accessible names. Windows control names and browser progress names/values follow English/Czech selection.

Periodic updates preserve focus on an action that still exists. If the action disappears because the request completes, is revoked or removed, it cannot remain focused. Open browser batch details are preserved across polling. These improvements support navigation but do not establish that every workflow is accessible with a screen reader.

## Verification scope

Automated checks cover preference normalization/storage, manual/System changes, English/Czech names, computed normal text contrast, keyboard focus, reduced motion, a Chromium forced-colors emulation and real transfer workflows. Native checks verify palette resources, system/high-contrast mapping without changing host preferences, saved preference loading, contextual automation names and focus retention. Own-window native screenshots and mobile browser screenshots are saved by CI.

The built-in normal palettes target at least **4.5:1** for tested normal text pairs; the browser also tests meaningful control/focus contrast. W3C describes the [minimum text contrast criterion](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) and [visible focus](https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html). Native system resource behavior follows [WPF system resources](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-how-to-use-system).

This is not a complete WCAG audit or certification. Actual Narrator/NVDA, Android TalkBack/iOS VoiceOver, user-selected Windows contrast themes, large text/DPI and all keyboard workflows remain manual verification items. Browser emulation does not reproduce phone assistive technology. See [release matrix](RELEASE-TEST-MATRIX.md).
