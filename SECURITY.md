# Security policy

LAN Drop is an early development preview. No version is currently supported as a security-hardened stable release.

## Preview boundaries

The Windows application defaults to HTTPS for phone connections. A valid password-protected PFX must have a private key, current validity, explicit server-authentication usage and a subject-alternative IP name matching the selected address. TLS uses versions 1.2/1.3. Secure mode binds HTTP only on loopback for authenticated administration and has no plaintext LAN fallback; QR links use the separate HTTPS port and HTTPS download cookies have Secure, HttpOnly and Strict flags. Arbitrary configured URL endpoints cannot add plaintext listeners.

Local certificate generation creates an independent thirty-day authority and server identity. Only the public authority certificate is exported, together with an AES-256/SHA-256 password-encrypted server PFX; the authority signing key is disposed without export. Output creation never overwrites existing files and rolls back its own partial outputs. Passwords are not persisted in desktop settings or placed in process arguments. Windows Schannel needs the leaf key in its user's cryptographic provider temporarily; ordinary disposal removes it, but forced process termination can leave provider artifacts. No trusted-root store is changed by the application.

The phone must trust the correct authority through an explicit, verified setup procedure. Trusting a new root changes the device's trust configuration and requires checking its origin and fingerprint; never send the server PFX to a phone or obtain initial trust from unauthenticated LAN traffic. Address changes/expiry require replacement and renewed trust. Physical phone setup remains unverified. See [HTTPS setup](docs/HTTPS.md).

Desktop certificate preflight uses an ephemeral leaf key and verifies local validity, server usage and selected IP without changing trust stores. Companion authority details are exposed only after a no-download custom-root chain validates their association with the leaf. A Ready result is not a phone trust or reachability test. Active certificate expiry is monitored periodically and stops desktop sharing when detected; the browser-only server does not perform desktop monitoring.

Manual diagnostic export serializes only explicit version/category/status/count fields from typed input, including bounded recovery counts in schema 2. It excludes raw settings, logs, exceptions, identities, addresses, paths, filenames, contents, sizes, fingerprints, passwords, tokens and links. Export saves locally through a user-selected dialog and sends nothing. Counts and platform versions can still describe usage and environment; review the summary before sharing it. See [diagnostic scope](docs/DIAGNOSTICS.md).

The browser-only server without a configured certificate and the explicit desktop HTTP preview still use unencrypted HTTP. A network attacker can observe or modify that traffic. Pairing approval and recipient isolation do not replace authenticated encryption. Use trusted private networks and do not expose the service to the internet.

The desktop controls the child server through authenticated loopback requests. LAN clients cannot change local settings. Only explicitly assigned outbound files are available to a recipient. Approved phones must obtain PC approval for each incoming selection, bound to their identity and each file's name and declared size. Each file grant is consumed before upload, cannot be replayed and does not prove the contents are safe. Pending offers expire after ten minutes; approved batches expire after two hours or earlier device expiry/revocation. Resource limits include 32 live batches, 1,000 live offers and bounded terminal metadata. Batch creation checks quota/file-count but does not reserve disk space; actual upload admission checks remain authoritative.

Activity is limited to 200 entries in the current server run and is owner-isolated for phone clients. Retry is deliberate and uses new approval; confirmed files are skipped. If a success response is lost, a saved file may still exist and a manual retry can create a duplicate. Check the PC list before retrying. Source selections are kept only in the current tab/app; no source paths or tokens are included in phone activity records.

Optional persistent local completion history is off by default. It stores at most 200 actual completed filenames, sizes, directions and timestamps in ordinary local JSON, with separate record IDs and no device identities, paths, contents or credentials. Filenames can contain personal information. Only local authenticated administration can change or clear history; phones cannot query it. The desktop can read its validated snapshot locally while sharing is stopped. History never restores grants, sessions or file availability. Disable retains older records; clear removes metadata only. Bounded schema validation, path checks, an exclusive writer lease and atomic checkpoint replacement protect normal operation; malformed/unknown storage is preserved as unavailable. A history failure does not invalidate a completed transfer response. History is not proof of a phone download or later file integrity, nor of power-loss durability. See [history controls](docs/HISTORY.md).

No automatic opening of received files, router port mapping or telemetry. Server startup console output contains an administrator secret and must not be shared.

Uploads require a sender SHA-256 checksum. The server compares the streamed bytes and rereads the closed temporary file before committing it. Clients require a matching completion receipt. This detects accidental corruption; it does not authenticate unencrypted HTTP or stop an attacker replacing both data and checksum. Verification is at commit time, not a guarantee about later edits, Safari downloads or persistence after power loss.

Desktop sharing checks the selected Windows profile before starting and blocks public/unknown profiles. While sharing, a bounded background query runs every fifteen seconds; an unavailable address or public/unknown profile stops the server. Detection is periodic and can be delayed. The browser-only server does not apply this gate or monitoring. Diagnostics do not modify firewall rules or prove reachability.

Storage checks reject reparse points in destination paths, reserve space for concurrent uploads and retain a free-space buffer. They do not provide atomic protection against path replacement by a hostile local process or prevent unrelated programs consuming disk space. Crash cleanup only removes marked inactive outboxes with an available exclusive lease; completed inbox files and unmarked folders are preserved. New incoming uploads use same-volume owned staging. Startup and explicit destination selection discard only bounded, strictly marked inactive staging with an available exclusive lease, known payload names and no unexpected or linked entries. Live runs, completed received files, historical loose partial files and unsafe/unknown staging are preserved. Graceful shutdown cancels upload reads before releasing staging leases. Cleanup is best-effort and may leave refused staging; it never recursively deletes. See [recovery](docs/RECOVERY.md).

## Reporting

Do not post sensitive files, tokens or a working exploit with private data in a public issue. Use the repository's private vulnerability reporting feature if available. If unavailable, ask the maintainer for a private reporting channel using a public issue without exploit details.

No response-time or bounty commitment is offered.

## Stable-release gates

- Authenticated encrypted transport proven on the supported phone/browser combination.
- Recipient authorization and revocation, bounded resources and path-safe storage.
- Signed distribution and authenticated update validation.
- Physical-device/network testing and review of the implemented threat model.
