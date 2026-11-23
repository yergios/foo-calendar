# Data protection

This document is for whoever **deploys** Foo Calendar. It lists every piece of personal
data the app stores, where data leaves the system, what the app does on your behalf, and
what remains yours to decide.

One thing to be clear about first, because it changes what you should expect from this
page: **no software is "GDPR compliant" on its own.** Compliance is a property of an
operator's practices — a lawful basis, a record of processing, retention decisions,
handling data-subject requests. What software can be is *compliance-enabling*: ship
defaults that don't create problems, and provide the mechanisms compliance needs. That is
what this app aims at, and this document is the part you need to do the rest.

**Who is who.** If you self-host this, you are the **controller** for the data in it. The
project's author distributes software and never receives your data — not a processor, not
a sub-processor, nothing to sign. Your processors are the services *you* point it at: your
identity provider, your hosting.

---

## What the app stores

### People (`Person`)

| Field | Why it exists |
|---|---|
| `Username` | Identity. Comes from your identity provider's configured username claim. |
| `FirstName`, `LastName`, `DisplayName` | Shown on the calendar row and anywhere a person is named. |
| `HomeCity` | An ISO 3166-2 region code, e.g. `CH-ZH`. Decides which public holidays apply to them. Not an address. |
| `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` | Who set this record up and when. |

### Absences (`Absence`)

Dates, a type, an optional note, an optional start/end time, and who created and last
updated it. There is no approval state: an absence is in effect the moment its owner adds
it ([0014](decisions/0014-remove-absence-approvals.md)).

**There is deliberately no "sick" type.** An absence's type is visible to everyone on the
team, and "this person is ill" is health data — a special category under GDPR Article 9,
with a materially higher bar than everything else here. The types are `Vacation`,
`Appointment`, and `Other`; illness is recorded as `Other`, which says someone is away
without saying why. Consequently **this app is not designed to store special-category
data**, and you should keep it that way.

The one place that guarantee can leak is the free-text **note**. It is the only field
whose contents nobody can predict, and a person can type anything into it. Two things
follow:

- Notes are shown to their author only. The API leaves them out of every response to
  anyone else, admins included, rather than leaving the browser to hide them. Nothing
  transmits them anywhere, and the audit log, application logs and traces never record
  them.
- Tell your team not to put health details, diagnoses, or anything similar in a note. That
  is the one instruction this app's design depends on and cannot enforce.

### Audit log (`AuditEntry`)

Append-only. Records who did what, when, and to what: the actor's username, an action name,
the target as it read at the time, and a short description. Nothing in the app updates or
deletes an entry.

It **deliberately outlives the people it names.** That is the point of an audit log — the
question it answers is usually about something that no longer exists — but it does mean a
deleted person's username and display name survive their profile. Two things make that
defensible rather than merely convenient:

1. A **lawful basis you state**, normally legitimate interest in being able to account for
   changes to a shared team record.
2. A **bound**, which is `Retention:AuditEntryDays` (default 365). Without it the log would
   become a permanent record of everyone who has ever worked for you.

Absence notes are never written to the audit log.

### In the browser

The SPA keeps copies in each member's browser so a repeat visit paints before any request
completes ([0011](decisions/0011-client-side-stale-while-revalidate.md)). In `localStorage`,
under the `foo-calendar:` key prefix:

- the people list and the year's absences and bank holidays, as the API served them to
  that member (so with their own notes only);
- their own profile;
- interface preferences: how they sort, filter and order the team.

In `sessionStorage`, under the same prefix, the page to return to after signing in. The
OIDC library (`oidc-client-ts`) keeps the sign-in session, tokens included, in
`sessionStorage`, and the state of a sign-in in progress in `localStorage`, both under the
app's prefix too (`foo-calendar:oidc.`).

**Signing out** removes all of it: the app stops writing to either storage, deletes every
key under its prefix in both, the library's session and sign-in state with them, and when
the identity provider advertises an end-session endpoint the browser is sent there to end
the provider's session too. On a
shared computer that is what keeps the next person from seeing the last one's calendar
copy, or their preferences.

---

## Where data leaves the system

| Destination | What goes | Notes |
|---|---|---|
| **Nager.Date** (`https://date.nager.at`) | **No personal data.** Country and region codes only, to fetch public holidays. | Switch it off with `Holidays:Source=None`, or use `File` for an air-gapped network. |
| **Your identity provider** | Standard OIDC sign-in and sign-out traffic | Not applicable when running without one. |
| **Your OTLP collector**, if you configure one | Traces, metrics and logs: request paths, timings, status codes, log messages, and the background jobs' own spans and metrics, which carry country codes, years, counts and outcomes only. Never an absence note. | Off until `Telemetry:OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set; see [deployment](deployment.md). The collector is your processor. |

Nothing else leaves. There is no telemetry to the project's author, no analytics, and no
outbound call this list doesn't name.

---

## Retention

Set under `Retention:` (see `src/Api/Configuration/RetentionOptions.cs`). A background job
sweeps daily and deletes what has aged out.

| Setting | Default | Rationale |
|---|---|---|
| `AuditEntryDays` | `365` | Long enough to answer "who changed that" a season or two later; short enough that the log doesn't become permanent. |
| `AbsenceDays` | `0` (off) | Looking back over previous years is what a leave calendar is *for*. Deleting absence history by default would be deleting the product. Set e.g. `1095` for three years if you have decided you don't need more. |

`0` means "keep indefinitely" for either of them.

**There is no "GDPR number" here.** Article 5(1)(e) says personal data must be kept no
longer than is necessary for the purpose it was collected for, and the purpose is yours to
state. These defaults are a reasonable starting point to be justified, not a compliance
guarantee.

---

## Data subject requests

- **Access / portability (Art. 15, 20).** A person's own data is on their profile page and
  their calendar row. Admins can export the people list as CSV. For a complete extract,
  query the SQLite database directly — it is one file.
- **Rectification (Art. 16).** Everyone can edit their own profile; admins can edit
  anyone's.
- **Erasure (Art. 17).** Deleting a person from the admin people page **cascades to their
  absences** — this is declared explicitly in `AppDbContext`, not left to
  a framework default. Their audit entries survive, per the section above. If you have
  concluded that erasure must reach the audit log too, delete those rows directly; nothing
  in the app depends on them.
- **Objection / restriction (Art. 18, 21).** Handle out of band.

---

## Security

- Authentication is OIDC against your provider, with per-permission authorization enforced
  server-side on every request. The frontend's own permission checks only decide what the
  interface *offers*; they are not the gate.
- `Auth:Provider=None` bypasses authentication entirely and authenticates every request as
  the fixed `Auth:DevUser` identity. It exists so the app can be run locally with no OIDC
  tenant, and the API **refuses to start with it outside Development** — there is no
  supported way to deploy this app without an identity provider.
- Data at rest is a single SQLite file. Encrypting it is your host's job — disk encryption,
  a volume, whatever you already do.
- The app has no password of its own to leak: it stores no credentials.

---

## What you still have to do

Nothing in this repository does any of this for you:

1. Decide and record your **lawful basis** for each purpose (usually legitimate interest or
   employment-contract necessity — not consent, which is hard to make freely given between
   an employer and staff).
2. Write your **privacy notice** and give it to your team. This document is the raw
   material; it is not that notice.
3. Keep your **record of processing** (Art. 30), if you need one.
4. Sign a **processor agreement** (Art. 28) with your host.
5. Set the **retention periods** above to something you can justify.
6. Tell your team **not to put health details in absence notes**.
