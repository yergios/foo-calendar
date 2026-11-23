# Regions and holidays

Which regions the calendar tracks, and where their public holidays come from.

## Which regions the calendar tracks

Nothing to configure, and no code changes: the set of tracked regions is **derived from
where people say they are**. Whoever sets `ES-MD` on their profile is what makes Madrid a
region this calendar knows about.

The only configured region is the team's own:

```json
"Team": { "HomeRegion": "CH-ZH" }
```

`Team:HomeRegion` is required, ISO 3166-2, and a bare country code (`"CH"`) is valid if the
team doesn't care about subdivisions. It is the one region synced whether or not anyone has
picked it, and the one region never dropped when the last person in it leaves.

Everything else follows from that:

| Event | What happens |
| --- | --- |
| Someone picks a region on their profile | Its country's and its own holidays are synced immediately, for this year and next |
| Someone changes region | The new one is synced; the old one is dropped if they were the last person in it |
| The last person in a region leaves or is deleted | That region's holidays are **deleted, manual entries included** |
| The last person in a country leaves | The country's nationwide holidays go too — but only once nobody is left in *any* of its regions |
| Weekly, and on startup | Everywhere the team currently is gets re-synced, current year and next |

> [!WARNING]
> Deleting a person deletes the bank holidays of any region they were the last occupant of,
> and that includes hand-added ones. `Team:HomeRegion` is exempt; nothing else is.

**Picking a region.** The profile page offers everything the holiday source knows about —
all 204 countries for Nager, and the subdivisions of whichever one is selected. The calendar's
region filter and the manual-holiday form offer only the regions the team occupies, and the
API rejects a manual holiday for anywhere else.

**Region names** come from a bundled ISO 3166-1/3166-2 name table
(`src/Api/Data/regions.json`, derived from [CLDR](https://cldr.unicode.org/), see
`NOTICE.txt`), because the Nager API returns subdivision *codes* with no names attached. A
code missing from that table falls back to showing the raw code.

## Where holidays come from

Public holidays are filled in by a configurable source. Whichever one is in use, the app owns the result: rows are stored locally, admins can override any of them by hand, and **a manual entry is never overwritten by a later sync**.

```json
"Holidays": {
  "Source": "Nager",
  "SyncInterval": "7.00:00:00",
  "Nager": { "BaseUrl": "https://date.nager.at/api/v3/" },
  "File": { "Path": "" }
}
```

| `Source` | Behaviour |
| -------- | ------------------------------------------------------------------------------------- |
| `Nager` | Default. Pulls from the [Nager.Date](https://date.nager.at/) API. Needs outbound internet. |
| `File` | Reads a JSON file. No egress, and it can express closures no public API knows about. |
| `None` | No automatic holidays at all. Whatever admins add by hand is all there is. |

Every region the team occupies is synced on startup, then every `SyncInterval` (a `d.hh:mm:ss` span), for the current year and the next. An admin can also trigger it from `/admin`, which with `Source=None` refuses with a `409` rather than pretending to have synced.

A source is asked for one country at a time, so a failed fetch takes every region of that country with it, and all of them come back in `failedRegions`. Re-running just those is what the region selector on the admin sync panel is for — `POST /api/admin/bank-holidays/sync?year=2026&region=ES-MD` — rather than re-fetching every other country to get at one.

**Behind a proxy?** No configuration here: `HttpClient` reads the standard `HTTP_PROXY`, `HTTPS_PROXY` and `NO_PROXY` environment variables by itself.

### The file source

Point it at a JSON file, typically mounted in:

```bash
Holidays__Source=File
Holidays__File__Path=/etc/foo-calendar/holidays.json
```

```json
[
  { "region": "CH", "date": "2026-08-01", "name": "Swiss National Day" },
  { "region": "CH-ZH", "date": "2026-04-13", "name": "Sechselauten" },
  { "region": "CH", "date": "2020-12-24", "name": "Office closed", "recurringAnnually": true }
]
```

- `region` and `date` are required; an entry missing either makes the file malformed. `name` and `recurringAnnually` are optional.
- `region` is an ISO 3166-2 code. A country code applies nationwide; a subdivision code applies to that region only. Entries for regions nobody on the team is in are ignored on sync.
- `recurringAnnually` ignores the year in `date` and applies its month/day to every year synced, which is what company closures usually are. (Feb 29 only lands on leap years.)
- The file is re-read on every sync, so edits take effect on the next one without a restart. A missing or malformed file is reported as a failed country on the admin page and logged — it never silently syncs nothing.

Rows created this way are labelled `File` in the API and the UI, and stamped `file-sync` in the audit columns, so it stays visible which system put a holiday there.
