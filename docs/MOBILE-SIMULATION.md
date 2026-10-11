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
| Original Unicode name chosen by the browser for download | Passed | **Not passed: ASCII fallback used** |
| Missing upload capabilities disable uploads while downloads remain available | Passed | Passed |
| Device revocation invalidates header access and the existing download cookie | Passed | Passed |

The Windows WebKit build suggested `PC _ fotografie _. 1.bin` for `PC – fotografie č. 1.bin`. The server supplied the original name in `Content-Disposition: filename*` and downloaded bytes matched exactly. The test reports this specific outcome as a limitation; it does not count original-name preservation as passed. Unexpected names still fail. Whether physical iOS Safari chooses the original name remains unverified. Do not infer that a Windows WebKit result proves an iPhone result.

The offline scenario interrupts the second request before its body reaches the server. It checks browser recovery and retained file references. It does not simulate every possible midstream Wi-Fi failure, device lock or power loss. Synthetic binary data does not test video decoding, gallery integration or the configured 10 GB ceiling.

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
