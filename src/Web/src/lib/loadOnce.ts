import { type Ref, shallowRef } from "vue";

import { readCache, writeCache } from "@/lib/browserCache";

/**
 * Wraps an async fetcher so concurrent callers share one in-flight request. Once it
 * resolves, every later call gets served instantly instead of re-fetching.
 * Only use this for fetchers whose resolved value is never legitimately `null`: this
 * wrapper uses `null` internally as its "not loaded yet" sentinel.
 *
 * Pass `cacheKey` for stale-while-revalidate across page reloads (best effort, via
 * localStorage): a cached value hydrates `data` for an instant first paint, but doesn't
 * count as loaded, so the first loadOnce() still fetches - that fetch is the revalidate.
 * The key must carry a schema version (e.g. "people.v1"), bumped whenever T's shape
 * changes, so an old browser's copy misses and refetches instead of being trusted with
 * fields it predates.
 *
 * `reconcile` merges a fresh response into the currently rendered value so that
 * unchanged data keeps its object identity and downstream caches/v-memo don't churn on a
 * revalidate that confirms the cache (see lib/reconcile.ts). Without it a fresh response
 * simply replaces the value wholesale.
 */
export function createLoadOnce<T>(
  fetcher: () => Promise<T>,
  cacheKey?: string,
  reconcile?: (previous: T, fresh: T) => T,
) {
  const cached = cacheKey ? readCache<T>(cacheKey) : null;
  // shallowRef: the value is only ever replaced wholesale (see the assignment below),
  // never mutated in place, so deep-proxying every object inside a fetched list (like
  // the whole team's people) would cost memory and proxy overhead for nothing.
  const data = shallowRef(cached) as Ref<T | null>;
  let loaded = false;
  let inFlight: Promise<T> | null = null;

  function loadOnce(): Promise<T> {
    if (loaded) return Promise.resolve(data.value as T);

    inFlight ??= fetcher()
      .then((value) => {
        const merged = reconcile && data.value !== null ? reconcile(data.value, value) : value;
        if (merged !== data.value) data.value = merged;
        loaded = true;
        if (cacheKey) writeCache(cacheKey, merged);
        return merged;
      })
      .finally(() => {
        inFlight = null;
      });

    return inFlight;
  }

  /** Forces the next loadOnce() call to re-fetch instead of returning the cached value. */
  function invalidate() {
    loaded = false;
  }

  return { data, loadOnce, invalidate };
}
