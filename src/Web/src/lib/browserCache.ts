const PREFIX = "foo-calendar:";

/** Reads and JSON-parses a cached value, or null if missing/unparseable/unavailable. */
export function readCache<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(PREFIX + key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch {
    return null;
  }
}

/** JSON-stringifies and stores a value. Best effort: silently no-ops if unavailable. */
export function writeCache<T>(key: string, value: T): void {
  try {
    localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    // Best effort only. A private-browsing session or a full storage quota shouldn't break the app.
  }
}

/** Removes a cached value. Best effort: silently no-ops if unavailable. */
export function clearCache(key: string): void {
  try {
    localStorage.removeItem(PREFIX + key);
  } catch {
    // Best effort only.
  }
}

/**
 * Removes every cached entry whose key starts with the given prefix. This is what makes
 * versioned and year-scoped keys safe to introduce: sweeping "absences:" before writing
 * "absences:2026.v1" evicts other years and any "absences:*.v0" a schema bump stranded,
 * so a stale copy can never outlive the code that knew its shape (the failure mode
 * described in stores/config.ts). Best effort, like everything else here.
 *
 * `except` spares one key from the sweep, for callers that evict a group's stale
 * versions while keeping the current one they're about to read.
 */
export function clearCacheGroup(prefix: string, except?: string): void {
  try {
    const full = PREFIX + prefix;
    const kept = except ? PREFIX + except : null;
    // Collect first: removing while indexing shifts localStorage's key order underneath.
    const doomed: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (key?.startsWith(full) && key !== kept) doomed.push(key);
    }
    for (const key of doomed) localStorage.removeItem(key);
  } catch {
    // Best effort only.
  }
}
