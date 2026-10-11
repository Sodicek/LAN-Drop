# HTTPS setup

The Windows app defaults to HTTPS for phone connections in 0.2.0-rc.2. Its administration channel remains authenticated HTTP on loopback only. The desktop chooses separate available ports and the phone's QR includes the HTTPS port. The browser-only server defaults to administration port 45821 and HTTPS port 45822. Secure mode does not expose a plaintext LAN listener or fall back to HTTP after a certificate error.

HTTPS requires the phone to trust the certificate authority that issued the PC's certificate. A QR code alone cannot establish that trust. The app neither installs a root certificate nor bypasses browser certificate checks. Physical Android/iPhone setup remains unverified; this candidate is not a stable compatibility guarantee.

## Create a local identity

1. Select the private/domain network interface that the phone can reach. Keep **Secure HTTPS** enabled.
2. Enter a new certificate password of at least twelve characters, then choose **Create local certificate** and a new `.pfx` file location on the PC. Existing output files are not overwritten. Prefer a local folder available only to your Windows account.
3. Keep the `.pfx` and its password on the PC. The adjacent `.ca.cer` contains only the public authority certificate; it is the only generated file to transfer to the phone. The app displays its SHA-256 fingerprint, which can be copied with the public certificate details.
4. Transfer the public `.ca.cer` through a trusted method, such as directly from the PC using your normal cable/file-transfer procedure. Verify its origin and SHA-256 fingerprint against the PC before trusting it. Do not obtain the initial trust certificate from an unauthenticated LAN download. Never send the `.pfx` or its password to the phone.
5. Install and explicitly trust the public authority using the phone's certificate settings. Managed devices may prohibit this; follow your administrator's instructions. If browser validation fails, stop and check the certificate, selected IP address, expiry and device trust instead of continuing through the warning.
6. Start sharing, scan the HTTPS QR, then approve the phone on Windows. The phone page displays its connection mode.

The app remembers the selected certificate path and HTTPS preference. It does not save the password; enter it again after restarting. Generated identities expire after thirty days and cover only the selected IPv4 address. Changing that address or renewing the identity requires a new certificate and manually replacing the phone's trusted public authority.

The signing key for the local authority is never exported and is disposed after issuing the server certificate. This means an existing authority cannot be used to renew another certificate. The server PFX protects its leaf key with AES-256/SHA-256 password encryption. Windows HTTPS imports that leaf key into the user's cryptographic provider temporarily because Schannel cannot use the ephemeral-key loading mode; normal certificate disposal releases it, but forced process termination can leave provider artifacts. No authority trust store is modified by the application.

## Phone certificate settings

On Android, use the device's certificate/credential settings to install a **CA certificate**, rather than a Wi-Fi/client certificate requiring a private key. Menu names and managed-device restrictions vary. Google's [certificate settings guide](https://support.google.com/pixelphone/answer/2844832/add-amp-remove-certificates) describes where credentials are managed; the generated public authority has no private key or password. Follow your device's CA installation instructions.

On iPhone/iPad, manually installed certificate profiles need explicit SSL trust. Apple describes **Settings → General → About → Certificate Trust Settings**, then enabling trust for the specific installed root in its [certificate trust guide](https://support.apple.com/en-us/102390). Installation and the actual Safari connection still need verification on hardware.

Trusting an authority affects the device's trust settings. Install only the public certificate whose origin you verified, and remove that specific authority when retiring the LAN Drop identity. Do not clear unrelated credentials.

## Use an existing certificate

Choose a password-protected server PFX from your administrator instead of generating one. It must have a private key, current validity, server-authentication usage and an IP subject-alternative name matching the selected IPv4 address. Its issuing authority must already be trusted by the phone. DNS-hostname certificates without that IP name are not supported by the current IP-based QR flow.

For the browser-only server, configure `HttpsCertificatePath` and `HttpsPort`, and supply the password only through the dedicated `LAN_DROP_CERTIFICATE_PASSWORD` environment variable. The desktop handles this child-process environment itself. Do not put passwords in command-line arguments, shell history, repository files or screenshots. Merely setting an HTTPS URL does not configure the certificate.

## Verification scope

`tests/secure_transport.py` uses a temporary authority trusted only by that test's TLS client. It verifies real HTTPS with normal certificate and hostname checks, untrusted/incorrect identities, secure download cookies, upload/download integrity and revocation. It does not install a root on Windows or skip validation. Native tests verify secure startup, QR routing, rejected configuration and password non-persistence.

Mobile browser emulation covers the interface and transfers over isolated test servers. The disposable Windows GitHub Actions runner also tests Chromium over HTTPS with the generated authority installed in that runner's user trust store and supplied to the test's request client; certificate checking stays enabled, and the specific test root is removed afterward. `tests/Run-TrustedBrowser.ps1` refuses to run outside Windows GitHub Actions. Local tests do not alter Windows trust. None of these checks proves phone certificate installation or Safari/Android HTTPS compatibility. See [mobile simulation](MOBILE-SIMULATION.md) and the [release matrix](RELEASE-TEST-MATRIX.md).

The explicit HTTP preview option remains available for trusted private-network testing. Its network traffic is unencrypted and its status is shown clearly. HTTPS encrypts traffic when validation succeeds; saved files are ordinary local files, and checksum receipts do not prove later phone storage or power-loss durability.
