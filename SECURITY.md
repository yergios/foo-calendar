# Security policy

## Reporting a vulnerability

**In a deployment you use.** Each deployment publishes its own contact at
`/.well-known/security.txt` (from `Team:SecurityContact`, falling back to
`Team:SupportUrl`). Report there whatever concerns how that deployment is run.

**In the software itself.** Open an issue in the
[repository's issue tracker](https://github.com/yergios/foo-calendar/issues) asking for a
private channel, and leave the exploit details out of the public issue.

## What is in scope

The [threat model](docs/threat-model.md) lists the trust boundaries, the mitigations and
the risks accepted on purpose. [Data protection](docs/data-protection.md) lists every piece
of personal data the app stores and everywhere data leaves it.
