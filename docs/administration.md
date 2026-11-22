# Administration

Day-to-day running of a deployment. In the web app, `/admin` has a section each for people (including bulk import), the bank-holiday sync, and the audit log, each shown only to someone holding its permission, see [Access control](access-control.md). The API reference lives at its own route.

There is nothing to approve: an absence is on the calendar the moment its owner adds it ([0014](decisions/0014-remove-absence-approvals.md)).

## Importing people in bulk

Onboarding a whole team one person at a time gets old around the tenth. `POST /api/admin/people/import` takes a CSV body and creates a person per row:

```bash
curl -X POST https://leave.acme.example/api/admin/people/import \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: text/csv" \
  --data-binary @people.csv
```

```csv
username,firstName,lastName,homeCity
jdoe,Jane,Doe,ES-MD
asmith,Alan,Smith,CH-ZH
```

**Only `username` is required**, and it has to match whatever `Auth:ClaimTypes:UserName` will carry for that person, since that is what links a sign-in to their record. Everything else is optional:

| Column | Notes |
| ------------- | ---------------------------------------------------------------------------- |
| `username` | Required. Must match the sign-in identity. |
| `firstName`, `lastName` | Up to 100 characters each. |
| `displayName` | Defaults to "First Last", or the username when neither is given. |
| `homeCity` | ISO 3166-2, e.g. `ES-MD` or just `ES`. Blank is fine. Importing someone into a region new to the team pulls its holidays in. |

Header names are matched loosely — case, spaces and underscores are ignored, so `Home City`, `home_city` and `homeCity` are the same column — and column order doesn't matter. Quoted fields, embedded commas and a UTF-8 BOM are all handled, so a spreadsheet export works as-is.

**Existing usernames are skipped, never updated.** That makes re-running the same file safe: fix what was reported and run it again, and the people who already made it through come back as skipped rather than duplicated. Editing an existing person stays a job for `/admin`.

Valid rows are imported even when others fail, and the response says exactly what happened, by line number:

```json
{
  "created": ["jdoe", "asmith"],
  "skipped": [{ "row": 5, "username": "ok", "reason": "Already imported, or already exists in the app." }],
  "failed": [{ "row": 6, "username": "broken", "reason": "The field HomeCity must match the regular expression ..." }]
}
```

## Who changed what

Rows carry `CreatedBy`/`UpdatedBy` for as long as they exist. The audit log covers the rest, and deletions in particular, where there is no row left to ask:

```bash
curl -H "Authorization: Bearer $TOKEN" \
  "https://leave.acme.example/api/admin/audit?limit=50&offset=0"
```

```json
{
  "entries": [
    {
      "id": 412,
      "occurredAt": "2026-08-13T09:14:02.113",
      "actor": "jdoe",
      "action": "person.deleted",
      "target": "Jane D.",
      "details": "jdoe, ES-MD; 7 absence(s) deleted with them"
    }
  ],
  "total": 413
}
```

It records profile creates, updates, and deletes; bulk imports; created, updated, and deleted absences; every hand edit to a manual bank holiday; and hand-triggered syncs. It is append-only — nothing in the app updates or deletes an entry, and a person's entries deliberately outlive their profile. The weekly sync writes nothing: it would bury the actions people actually took, and its rows already say `<source>-sync`.

An absence's note is never recorded — neither the log nor the admin page will show it to you. An entry says an Other absence from the 2nd to the 6th was created, not why. Entries written before approvals were removed may still say an absence was approved or rejected; they stay until retention removes them.

> Entries are deleted after `Retention:AuditEntryDays` (a year by default). That bound is also what keeps a deleted person's name from surviving in the log indefinitely — see [docs/data-protection.md](data-protection.md). Set it to `0` to keep them forever.

## Browsing the API

The API describes itself as OpenAPI at `/openapi/v1.json`, and serves [Scalar](https://scalar.com/) over it at `/scalar` — a reference you can read and call the API from, rather than a page of tables.

```bash
ApiDocs__Enabled=true       # off by default outside Development
ApiDocs__RoutePrefix=/scalar
```

It is **on in Development** and off everywhere else, because both routes are anonymous: a browser opening them carries no bearer token, so requiring one would serve a 401 to the only thing that ever opens them. Turning it on publishes the shape of the API — paths, parameters, schemas — to anyone who can reach the app. Never its contents: every route behind it still wants a token, and the fallback policy is unchanged. On an internal deployment whose users script against the API, that trade is usually worth making; think about it on one that faces further than that.

The document names a `Bearer` security scheme, so Scalar's auth panel takes an access token and sends it with every call you make from the page. Get one from your identity provider the way the SPA does, or from the browser devtools of a signed-in session.

Endpoints are grouped by tag — Absences, People, Bank holidays, Regions, Admin, Config — which is what the sidebar is built from. A new endpoint inherits its group's tag from the `MapGroup` call, so there is nothing to remember per route.

> Under a sub-path (`ReverseProxy:PathBase`), Scalar resolves the document URL against the page's own path, so `/calendar/scalar` reads `/calendar/openapi/v1.json` without either being configured.
