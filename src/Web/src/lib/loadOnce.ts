import { type Ref, shallowRef } from "vue";

import { readCache, writeCache } from "@/lib/browserCache";

/**
 * Wraps an async fetcher so concurrent callers share one in-flight request. Once it
 * resolves, every later call gets served instantly instead of re-fetching.
 * Only use this for fetchers whose resolved value is never legitimately `null`: this
 * wrapper uses `null` internally as its "not loaded yet" sentinel.
 *
 * Pass `cacheKey` if you also want the value persisted across page reloads (best
 * effort, via localStorage), so it's ready before the first fetch even finishes.
 */
export function createLoadOnce<T>(fetcher: () => Promise<T>, cacheKey?: string) {
  const cached = cacheKey ? readCache<T>(cacheKey) : null;
  // shallowRef: the value is only ever replaced wholesale (see the assignment below),
  // never mutated in place, so deep-proxying every object inside a fetched list (like
  // the whole team's people) would cost memory and proxy overhead for nothing.
  const data = shallowRef(cached) as Ref<T | null>;
  let loaded = cached !== null;
  let pending: Promise<T> | null = null;

  function loadOnce(): Promise<T> {
    if (loaded) return Promise.resolve(data.value as T);

    pending ??= fetcher()
      .then((value) => {
        data.value = value;
        loaded = true;
        if (cacheKey) writeCache(cacheKey, value);
        return value;
      })
      .finally(() => {
        pending = null;
      });

    return pending;
  }

  /** Forces the next loadOnce() call to re-fetch instead of returning the cached value. */
  function invalidate() {
    loaded = false;
  }

  return { data, loadOnce, invalidate };
}
