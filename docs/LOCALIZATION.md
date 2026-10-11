# Localization

English is the default, Czech is supported and Automatic follows the device's language with English fallback. Windows and phone preferences are independent.

## Web client

Add a complete dictionary to `src/LanDrop/wwwroot/i18n.js`, register its locale/name in the language selector, and extend automatic matching in `locale()`. Keep keys and named interpolation parameters identical to English. Translate backend `code` messages as well as static labels and dynamic states.

## Native Windows

Add `src/LanDrop.Desktop/Locales/<code>.json` with the same keys as English. These JSON files are embedded at build time. Register the language choice and automatic selection in MainWindow. Unsupported locales fall back to English.

Both clients use complete messages rather than assembling translated sentence fragments. Preserve accessibility labels and number/time formatting. Avoid translating names, paths, tokens and diagnostic codes.

## Check

```powershell
node tests/localization.cjs
```

The current test asserts English/Czech dictionary and parameter parity plus frontend/server message coverage. Extend its registered-language checks when adding a locale. Test translated layouts on a phone and in the desktop window; key parity is not visual QA.
