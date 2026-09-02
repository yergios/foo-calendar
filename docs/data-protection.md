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
SMTP provider, your webhook receiver, your hosting.

---

## What the app stores

### People (`Person`)

| Field | Why it exists |
|---|---|
| `Username` | Identity. Comes from your identity provider's configured username claim, or is self-asserted when the deployment runs without one. |
| `FirstName`, `LastName`, `DisplayName` | Shown on the calendar row and anywhere a person is named. |
| `Email` | Notification delivery, and nothing else. Optional — a person with no address simply gets no email. |
| `HomeCity` | An ISO 3166-2 region code, e.g. `CH-ZH`. Decides which public holidays apply to them. Not an address. |
| `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` | Who set this record up and when. |
| `EmailOn…` (three flags) | Per-person email preferences. **Off by default**; nobody is subscribed to anything without asking. |

### Absences (`Absence`)

Dates, a type, an optional note, an optional start/end time, and who created, updated, and
reviewed it.

**There is deliberately no "sick" type.** An absence's type is visible to everyone on the
team, and "this person is ill" is health data — a special category under GDPR Article 9,
with a materially higher bar than everything else here. The types are `Vacation`,
`Appointment`, and `Other`; illness is recorded as `Other`, which says someone is away
without saying why. Consequently **this app is not designed to store special-category
data**, and you should keep it that way.

The one place that guarantee can leak is the free-text **note**. It is the only field
whose contents nobody can predict, and a person can type anything into it. Two things
follow:

- Notes never leave the app. They are excluded from webhooks unconditionally, with no
  setting to change that, and the audit log never records them.
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

### Notifications (`Notification`)

One per reviewed absence request: the person, the type, the dates, and the reviewer's note
on a rejection. Deleted after `Retention:NotificationDays` (default 90).

---

## Where data leaves the system

| Destination | What goes | Notes |
|---|---|---|
| **Your SMTP server** | Recipient name and address; who is off, and when | Only if `Email:Host` is configured. Only to people who opted in. Your SMTP provider is your processor. |
| **Your webhook URL** | Event type, actor, person id and name, dates, absence type and status, times | Only if `Webhook:Url` is set. **Never notes.** Whatever receives this — Slack, Teams, a script, Zapier — is your processor, and usually a far wider audience than the people an absence was shared with. Point it somewhere you have thought about. |
| **Nager.Date** (`https://date.nager.at`) | **No personal data.** Country and region codes only, to fetch public holidays. | Switch it off with `Holidays:Source=None`, or use `File` for an air-gapped network. |
| **Your identity provider** | Standard OIDC sign-in traffic | Not applicable when running without one. |

Nothing else leaves. There is no telemetry to the project's author, no analytics, and no
outbound call this list doesn't name.

---

## Retention

Set under `Retention:` (see `src/Api/Configuration/RetentionOptions.cs`). A background job
sweeps daily and deletes what has aged out.

| Setting | Default | Rationale |
|---|---|---|
| `AuditEntryDays` | `365` | Long enough to answer "who changed that" a season or two later; short enough that the log doesn't become permanent. |
| `NotificationDays` | `90` | These are read once and never referred to again. |
| `AbsenceDays` | `0` (off) | Looking back over previous years is what a leave calendar is *for*. Deleting absence history by default would be deleting the product. Set e.g. `1095` for three years if you have decided you don't need more. |

`0` means "keep indefinitely" for any of them.

**There is no "GDPR number" here.** Article 5(1)(e) says personal data must be kept no
longer than is necessary for the purpose it was collected for, and the purpose is yours to
state. These defaults are a reasonable starting point to be justified, not a compliance
guarantee.

---

## Data subject requests

- **Access / portability (Art. 15, 20).** A person's own data is on their profile page and
  their calendar row. Admins can export the people list as CSV. For a complete extract,
  query the SQLite database directly — it is one file.
- **Rectification (Art. 16).** Everyone can edit their own profile; admins can edit anyone's.
- **Erasure (Art. 17).** Deleting a person from the admin people page **cascades to their
  absences and notifications** — this is declared explicitly in `AppDbContext`, not left to
  a framework default. Their audit entries survive, per the section above. If you have
  concluded that erasure must reach the audit log too, delete those rows directly; nothing
  in the app depends on them.
- **Objection / restriction (Art. 18, 21).** Handle out of band.

---

## Security

- Authentication is OIDC against your provider, with per-permission authorization enforced
  server-side on every request. The frontend's own permission checks only decide what the
  interface *offers*; they are not the gate.
- `Auth:Provider=None` turns authentication off entirely and needs
  `Auth:AllowUnauthenticatedDeployment=true` to run outside development. In that mode
  **anyone who can reach the app is anyone they choose to be**, and the roster of names is
  readable before choosing. It exists for networks where reaching the app is already the
  access control — a private LAN, a VPN, a tailnet. If that is not true of your network, it
  is the wrong setting, and running it there is a breach waiting to be written up.
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
4. Sign **processor agreements** (Art. 28) with your SMTP provider, your host, and whoever
   receives your webhooks.
5. Set the **retention periods** above to something you can justify.
6. Tell your team **not to put health details in absence notes**.
