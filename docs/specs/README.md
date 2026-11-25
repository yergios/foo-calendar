# Change specs

[SPEC.md](../../SPEC.md) describes what the system must do. A change spec describes one change
to that: which requirements it adds, changes or retires, the work in order, and how it will be
verified. It is written before the code and is what a build session works from.

- Write one with the `/spec` command, which interviews for it, or by hand in the shape of an
  existing one. Number it with the next free number.
- A change spec is done when its "Done when" list holds. The commit that finishes it updates
  `SPEC.md` and sets the spec's status to Done.
- A change that settles a question with alternatives worth remembering also gets a
  [decision record](../decisions/README.md).

| # | Change | Status |
| --- | --- | --- |
| [0001](0001-remove-approvals-and-close-review-gaps.md) | Remove approvals and close the review gaps | Done |
| [0002](0002-test-every-route-and-the-hard-cases.md) | Test every route and the hard cases | Done |
| [0003](0003-nullable-types-and-domain-errors.md) | Nullable types and domain errors | Done |
| [0004](0004-headers-telemetry-performance-e2e.md) | Headers, telemetry, performance and end-to-end checks | Done |
| [0005](0005-agent-tooling.md) | Agent tooling: one check, enforced hooks, automatic privacy review | Done |
| [0006](0006-operator-contact-required.md) | The operator's contact is required | Pending |
