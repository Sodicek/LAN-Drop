# Security policy

LAN Drop is an early development preview. No version is currently supported as a security-hardened stable release.

## Preview boundaries

Transfers and authentication currently use HTTP. A network attacker can observe or modify traffic. Pairing approval and recipient isolation do not replace authenticated encryption. Use only a trusted private network; do not expose the service to the internet.

The desktop controls the child server through authenticated loopback requests. LAN clients cannot change local settings. Only explicitly assigned outbound files are available to a recipient. Approved phones can upload to the inbox.

No automatic opening of received files, router port mapping or telemetry. Server startup console output contains an administrator secret and must not be shared.

## Reporting

Do not post sensitive files, tokens or a working exploit with private data in a public issue. Use the repository's private vulnerability reporting feature if available. If unavailable, ask the maintainer for a private reporting channel using a public issue without exploit details.

No response-time or bounty commitment is offered.

## Stable-release gates

- Authenticated encrypted transport proven on the supported phone/browser combination.
- Recipient authorization and revocation, bounded resources and path-safe storage.
- Signed distribution and authenticated update validation.
- Physical-device/network testing and review of the implemented threat model.
