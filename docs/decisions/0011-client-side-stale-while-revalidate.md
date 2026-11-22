# 11. Cache in the client with stale-while-revalidate

- Status: Accepted
- Date: 2026-09-02
- Source: commit `d5426d9`, `src/Web/src/lib/` (`reconcile.ts`, `loadOnce.ts`, `browserCache.ts`);
  response compression from commit `70ad050` (2026-08-20)

## Context

Cold load was a ladder of serial network legs with an idle CPU between them, and every
absence save refetched both year endpoints and re-rendered every row. A year of absences for
a real team is several hundred KB uncompressed.

## Decision

- **Paint from cache, then revalidate.** Absences, holidays and people are stored in
  `localStorage` under version-suffixed keys, year-scoped for absences and holidays. The
  page paints from them with no skeleton, then refetches in the background.
- **Keep object identity when nothing changed.** `reconcile.ts` maps refetched DTOs back to
  the previous objects when they are field-for-field equal, and whole arrays when nothing
  changed. A revalidation that confirms the cache re-renders nothing.
- **Patch mutations locally.** An absence save applies the server's response to local state
  instead of refetching the year, so one edit rebuilds one row.
- **Flatten the ladder.** Hashed assets are `immutable`, rarely used views are lazy chunks,
  and the remaining requests start in parallel.
- **Compress responses** (gzip and Brotli), which measured about 5x smaller for the
  absences payload. This one predates the rest: it was added in `70ad050`.

## Alternatives considered

- **HTTP ETags or output caching on API responses.** Rejected as redundant under the client
  cache: the page already paints from local data and revalidates in the background.
- **One combined endpoint for people and absences.** Rejected on 2026-09-01; the reasoning
  was not recorded. What holds now is that the two requests start in parallel and both
  paint from cache, so neither waits on the other.

## Consequences

- Repeat visits show the calendar immediately, before any request completes.
- Schema changes need a cache key version bump, and the version-suffixed keys plus group
  sweeps exist because a stale-schema cache once broke the config store.
- **Known limit, left deliberately:** `localStorage` holds about 5 MB per origin. Above
  roughly 450 people a year's absences no longer fit, and `writeCache` swallows the quota
  error, so caching silently stops and the app falls back to plain fetching. The fix, when
  a deployment gets near it, is IndexedDB or a trimmed DTO.
