# 20. The operator's contact is required, and nothing ships in its place

- Status: Accepted
- Date: 2026-11-25

## Context

The in-app privacy notice and terms name the deployment's operator as the controller and
tell members to reach them at `Team:SupportUrl`. The same setting is the footer's Contact
link, the support link on the 401 and 403 pages, and the fallback for `security.txt`.

`appsettings.json` shipped `mailto:support@example.com` for it, so a deployment that never
set the value published a contact nobody reads: in the notice, in the terms, and as its
security contact. A deployment that cleared it got a notice with empty links where the
contact should be. GDPR (Article 13) asks the controller to give its identity and contact
details in the notice, so a notice with no working contact is incomplete either way.

## Decision

- **`Team:SupportUrl` is required outside Development,** as an absolute URL in any scheme
  (`mailto:`, `https:`, ...), with something after the scheme and no whitespace around
  it. Startup stops with a message naming the setting when it is missing, not absolute,
  only a scheme (`mailto:`), or padded (` mailto:...`, or a trailing newline from a secret
  file) (CFG-1, CFG-2). A padded value is refused rather than trimmed: the operator sees it
  once and fixes it, and nothing downstream has to remember to trim.
- **No value ships for it.** `appsettings.json` leaves it empty and `.env.example` leaves
  it for the operator to fill in, so copying the example without editing it stops startup
  rather than publishing a placeholder. Development supplies its own value in
  `appsettings.Development.json`.
- **A `mailto:` contact is shown as its address** in the notices: members read
  `it@acme.example`, not `mailto:it@acme.example`.

## Alternatives considered

Offered on 2026-11-25:

- **Keep the shipped placeholder.** Rejected: it publishes a contact that reaches nobody,
  and silently: nothing tells the operator it is still there.
- **The software's author's own address, fixed in the notices.** Rejected: the author is not
  the controller of any other deployment and never receives its data, as the notice itself
  says. Members' requests would go to the wrong party.
- **Keep it optional, with a fallback sentence in the notices when it is empty.** Rejected:
  a notice without the controller's contact is incomplete, and the operator is the one who
  can fix that. Stopping at startup tells them before any member reads it.
- **Reject reserved example domains** (`example.com`, `.test`, ...) as placeholders.
  Rejected: with no value shipped there is no placeholder left to catch, and a list of
  domains is one more rule to keep right.
- **Require it in Development too.** Rejected: a local run should start with no
  configuration, as it does for the identity provider settings.

Raised by the review of the build, decided on 2026-11-25:

- **Trim surrounding whitespace instead of refusing it.** Rejected: a padded value passed
  `Uri.TryCreate`, which trims, but reached the notices and `security.txt` as written, so
  a leading space showed the `mailto:` scheme. Trimming once where it is bound would work,
  but refusing it keeps one rule for every bad value: stop and name the setting.
- **Accept a scheme with nothing after it** (`mailto:`), since the value is not checked to
  reach anyone anyway. Rejected: it renders an empty link where the contact should be,
  the case this decision exists to prevent.

## Consequences

- An upgrade of a deployment that relied on the shipped value stops at startup until it
  sets `Team:SupportUrl`. The changelog says so.
- `security.txt` (CFG-7) always has a contact outside Development, since it falls back to
  this setting.
- The value is still not checked to reach anyone. That stays the operator's job.
