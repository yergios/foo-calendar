# Foo Calendar

[![CI](https://github.com/yergios/foo-calendar/actions/workflows/ci.yml/badge.svg)](https://github.com/yergios/foo-calendar/actions/workflows/ci.yml)

A self-hostable calendar for coordinating international teams. Tracking vacations, appointments, and other absences on a shared, year-at-a-glance calendar with automatic public holiday syncing per region.

One container, one process, one SQLite file, signing in through your own OIDC provider.

## Self-hosting

**[Deployment guide](docs/deployment.md)**: the [quickstart](docs/deployment.md#quickstart) has it running with Docker Compose in three steps, then covers configuration, reverse proxies and observability.

Handling personal data with it? Read [Data protection](docs/data-protection.md) before you deploy: it lists everything the app stores, everywhere data leaves, and what stays your responsibility as the controller.

## Documentation

**Using it**

- [User guide](docs/user-guide.md): the calendar, absences, bank holidays, your profile, and the FAQ.

**Running it**

- [Deployment](docs/deployment.md): quickstart, configuration, the container image, reverse proxies, scaling, observability.
- [Access control](docs/access-control.md): permissions, and granting them through your identity provider.
- [Regions and holidays](docs/regions-and-holidays.md): how tracked regions follow the team, and the holiday sources.
- [Administration](docs/administration.md): bulk import, the audit log, the API reference.
- [Data protection](docs/data-protection.md): every piece of personal data stored, where data leaves, what stays your responsibility.

**Developing it**

- [Developer guide](docs/development.md): setup, running locally, seeding, debugging, the commands and checks, migrations and releases.
- [Architecture](docs/architecture.md): the system, startup, the request pipeline, the absence lifecycle, the frontend.
- [Specification](SPEC.md): what the system must do, as numbered requirements traced to tests, with changes in progress in [docs/specs](docs/specs/README.md).
- [Decision records](docs/decisions/README.md): every significant design decision, with what was considered and rejected.
- [Threat model](docs/threat-model.md): trust boundaries, what is exposed, mitigations, and the risks accepted.
- [Developing with agents](docs/agentic-development.md): how the repository is built with coding agents, its specs, reviews and hooks, and what they caught.

[Changelog](CHANGELOG.md) · [Security policy](SECURITY.md)

## License

Foo Calendar is source-available under the [Elastic License 2.0](LICENSE.txt) (`Elastic-2.0`). In plain terms:

- **You may** read the source, run it, self-host it, and modify it — including inside a for-profit company, to coordinate that company's own teams.
- **You may** redistribute it and your modifications, as long as you pass along these terms and mark clearly what you changed.
- **You may** re-skin it to match your brand: colors, fonts and shape live in one file, and your team's own name goes in the header via `Team:OrgName`.
- **You may not** offer it to third parties as a hosted or managed service that exposes a substantial part of its functionality. Running it for your own staff is not this, and your company being profitable changes nothing.
- **You may not** strip or obscure the licensing and copyright notices, which includes the product name, mark and copyright line in the app's footer.
- **You may not** rename the product or call your fork "Foo Calendar". The license grants no rights in the name or the mark.

See [TRADEMARK.md](TRADEMARK.md) for what that means in practice, spelled out. The full text is in [LICENSE.txt](LICENSE.txt) and it is the only thing that governs; the summary above is not legal advice. [NOTICE.txt](NOTICE.txt) holds the copyright notice and the third-party attributions.
