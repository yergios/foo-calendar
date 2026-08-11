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
