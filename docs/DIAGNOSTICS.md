# Connection checks and diagnostic export

Before starting HTTPS sharing, select the intended network and certificate, enter its password, then choose **Check certificate**. The same check runs before **Start sharing**. A successful PC check verifies the local certificate's private key, validity, server usage and selected IP; it does not establish trust on a phone or prove network reachability.

The certificate summary shows its IP addresses and expiry, with a warning when seven days or less remain. Choose a replacement certificate after an IP change or expiry and set up its authority on the phone again. See [HTTPS setup](HTTPS.md) ([Czech guide](cs/HTTPS.md)). Sharing periodically checks the active certificate expiry and stops when it expires; monitoring is periodic and can be delayed.

For generated certificates, LAN Drop can recover the adjacent public `.ca.cer` after restarting and checking the certificate. It offers the public path and SHA-256 fingerprint only after verifying the issuing chain locally. Missing or unrelated companion certificates do not produce copyable authority details. This local association check does not mean that a phone trusts the authority. Never transfer a `.pfx` or password to a phone.

## Save a support summary

Choose **Save anonymous diagnostics**, select a local JSON destination, and inspect the saved report before attaching it to an issue. Export is manual and does not send anything. It is available with sharing stopped or running.

The report includes only an explicit list of fields:

- Application version, Windows version, process architecture and .NET runtime version.
- Sharing state, HTTPS/HTTP preview mode and selected network profile category.
- Certificate check status and whether a successfully checked certificate expires soon.
- Counts of connected/requesting devices, pending requests and recent transfers.

The report excludes addresses, network/PC/user/device names, source/destination/certificate paths, filenames, file sizes or contents, certificate fingerprints, passwords, pairing links, session/admin tokens, logs and exception messages. It has no transfer history or reachability test. An unchecked certificate is recorded as **NotChecked**. This is a small troubleshooting summary, not a guarantee of phone compatibility.

Do not attach server console output, desktop settings or private certificate files to a public issue. See [security policy](../SECURITY.md).
