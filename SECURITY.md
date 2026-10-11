# Security policy

LAN Drop is an early development preview. No version is currently supported as a security-hardened stable release.

## Preview boundaries

Transfers and authentication currently use HTTP. A network attacker can observe or modify traffic. Pairing approval and recipient isolation do not replace authenticated encryption. Use only a trusted private network; do not expose the service to the internet.

The desktop controls the child server through authenticated loopback requests. LAN clients cannot change local settings. Only explicitly assigned outbound files are available to a recipient. Approved phones must obtain separate PC approval for each incoming file, bound to their device identity, file name and declared size. Approval is consumed before upload, cannot be replayed and does not prove the contents are safe. Offers expire after ten minutes and are bounded to 32 live requests plus 128 recent terminal records.

No automatic opening of received files, router port mapping or telemetry. Server startup console output contains an administrator secret and must not be shared.

Desktop sharing checks the selected Windows profile before starting and blocks public/unknown profiles. While sharing, a bounded background query runs every fifteen seconds; an unavailable address or public/unknown profile stops the server. Detection is periodic and can be delayed. The browser-only server does not apply this gate or monitoring. Diagnostics do not modify firewall rules or prove reachability.

Storage checks reject reparse points in destination paths, reserve space for concurrent uploads and retain a free-space buffer. They do not provide atomic protection against path replacement by a hostile local process or prevent unrelated programs consuming disk space. Crash cleanup only removes marked inactive outboxes with an available exclusive lease; completed inbox files and unmarked folders are preserved. Incomplete inbox files may remain after a crash.

## Reporting

Do not post sensitive files, tokens or a working exploit with private data in a public issue. Use the repository's private vulnerability reporting feature if available. If unavailable, ask the maintainer for a private reporting channel using a public issue without exploit details.

No response-time or bounty commitment is offered.

## Stable-release gates

- Authenticated encrypted transport proven on the supported phone/browser combination.
- Recipient authorization and revocation, bounded resources and path-safe storage.
- Signed distribution and authenticated update validation.
- Physical-device/network testing and review of the implemented threat model.
