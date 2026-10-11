# Mobile browser simulation

LAN Drop's phone client runs in a browser. The automated suite uses actual Chromium and WebKit browser engines on Windows, with Playwright device profiles for Pixel 7 and iPhone 13. It connects to real isolated LAN Drop servers over loopback HTTP and compares saved bytes and SHA-256 receipts. This is browser emulation, not a booted Android/iOS operating system or a physical-device test.

## Results on 2026-10-11

| Scenario | Chromium / Pixel 7 profile | Windows WebKit / iPhone 13 profile |
| --- | --- | --- |
| EN/CS, suggested phone names and preservation of custom names | Passed | Passed |
| Portrait/landscape layout without horizontal overflow | Passed | Passed |
| Reload during pairing and after approval, retained session | Passed | Passed |
| File selection unavailable before device approval | Passed | Passed |
| Real worker hashing, upload and verified saved PC copy | Passed | Passed |
| One consent for twenty files, exact saved bytes | Passed | Passed |
| Cancel, clear selection and rejected batch write no files | Passed | Passed |
| First file completes; browser goes offline at second upload; manual retry requests only the unfinished file | Passed | Passed |
| Fresh retry consent and no duplicate of confirmed completed file | Passed | Passed |
| Empty file and 12 MiB synthetic binary across worker chunks | Passed | Passed |
| Cookie-authorized browser download, exact bytes and UTF-8 response filename | Passed | Passed |
| Original Unicode name chosen by the phone Download action | Passed | Passed in rc.2; corrected from rc.1 |
| Missing upload capabilities disable uploads while downloads remain available | Passed | Passed |
| Device revocation invalidates header access and the existing download cookie | Passed | Passed |

In rc.1 the Windows WebKit build suggested `PC _ fotografie _. 1.bin` for `PC – fotografie č. 1.bin`, preferring the ASCII header fallback. In rc.2 the server supplies only the UTF-8 disposition name for Unicode filenames and the existing phone Download action supplies the original name through its download attribute. Chromium and Windows WebKit now preserve that name, with exact downloaded bytes. The test requires the original name and no longer tolerates the ASCII fallback. Direct URL navigation without the phone Download action is not verified for Unicode names in Windows WebKit. Physical iOS Safari remains unverified.

The offline scenario interrupts the second request before its body reaches the server. It checks browser recovery and retained file references. It does not simulate every possible midstream Wi-Fi failure, device lock or power loss. Synthetic binary data does not test video decoding, gallery integration or the configured 10 GB ceiling.

## HTTPS verification

Local `tests/secure_transport.py` verifies real TLS using a temporary authority trusted only in that client's SSL context, including rejection of untrusted roots and wrong hostnames. It leaves Windows roots unchanged. The disposable Windows GitHub Actions runner additionally runs the Android/Chromium profile over HTTPS: `tests/Run-TrustedBrowser.ps1` creates an isolated certificate, installs its public authority only in that runner's user trust store, gives the test request client the same authority and removes the root afterward. The script refuses non-CI execution. Certificate validation is never disabled. This proves desktop Chromium's HTTPS flow, not phone CA installation or physical Android/iOS trust behavior.

## Run the checks

```powershell
dotnet build LanDrop.slnx -c Release
npm ci --ignore-scripts
npx playwright install chromium webkit
node tests/mobile_browser.cjs
```

Select a single profile with `android` or `iphone` as the final argument. Set `LAN_DROP_SCREENSHOT_DIR` to an output directory to save a full-page image and viewport image per profile. Tests create their own temporary data, port and backend process and remove them on completion. They do not access personal files or change the firewall.

## Remaining physical checks

Camera scanning of the real QR code, Android/iOS file pickers, storage permissions, system downloads, gallery visibility, OS background/lock behavior, real Wi-Fi/firewall connectivity and large-file performance still require hardware. The tests open the generated pairing link directly and inject files into the browser input. Keep those physical checks marked as not run in the [release test matrix](RELEASE-TEST-MATRIX.md).

Playwright explains [device emulation](https://playwright.dev/docs/emulation) and the [differences between its WebKit builds and Safari](https://playwright.dev/docs/browsers#webkit).
