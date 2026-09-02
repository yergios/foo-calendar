/** Field-by-field comparison for flat DTOs: every field a primitive or null. Own keys
 * only, both directions covered by the length check. */
export function shallowEqual(a: Record<string, unknown>, b: Record<string, unknown>): boolean {
  const keys = Object.keys(a);
  if (keys.length !== Object.keys(b).length) return false;
  return keys.every((key) => a[key] === b[key]);
}

/**
 * Merges a freshly fetched list into the one already rendered, keeping the identity of
 * everything that didn't change: the previous element object where a fresh item is
 * field-for-field equal, and the previous array itself when every position is. A refetch
 * parses fresh JSON, so every object in it is new even when nothing changed on the
 * server; identity is what the calendar grid's per-person cell cache and the rows' v-memo
 * compare, so unchanged data re-rendering nothing depends on this step, not on luck.
 */
export function reconcileList<T extends { id: number }>(previous: T[], fresh: T[]): T[] {
  const prevById = new Map(previous.map((item) => [item.id, item]));
  let identical = previous.length === fresh.length;
  const merged = fresh.map((item, i) => {
    const prev = prevById.get(item.id);
    if (prev && shallowEqual(prev, item)) {
      if (identical && previous[i] !== prev) identical = false;
      return prev;
    }
    identical = false;
    return item;
  });
  return identical ? previous : merged;
}
