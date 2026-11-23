/**
 * Every key the app writes to localStorage or sessionStorage starts with this, interface
 * preferences and the OIDC library's session included (see initAuth), so sign-out can
 * remove all of it (see clearAppStorage) without touching anything another app on the same
 * origin stored.
 */
export const STORAGE_PREFIX = "foo-calendar:";

// Set when sign-out starts and never unset, since sign-out ends by leaving the page.
let writesStopped = false;

/**
 * Makes every later writeCache a no-op. What sign-out calls first: a request already in
 * flight when it starts would otherwise write what it fetched back into the storage that
 * sign-out is clearing, where it would outlive the session.
 */
export function stopCacheWrites(): void {
  writesStopped = true;
}

// Collect first: removing while indexing shifts the storage's key order underneath.
function removeKeys(storage: Storage, doomed: (key: string) => boolean): void {
  const keys: string[] = [];
  for (let i = 0; i < storage.length; i++) {
    const key = storage.key(i);
    if (key !== null && doomed(key)) keys.push(key);
  }
  for (const key of keys) storage.removeItem(key);
}

/** Reads and JSON-parses a cached value, or null if missing/unparseable/unavailable. */
export function readCache<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(STORAGE_PREFIX + key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch {
    return null;
  }
}

/** JSON-stringifies and stores a value. Best effort: silently no-ops if unavailable, and
 * once sign-out has started. */
export function writeCache<T>(key: string, value: T): void {
  if (writesStopped) return;
  try {
    localStorage.setItem(STORAGE_PREFIX + key, JSON.stringify(value));
  } catch {
    // Best effort only. A private-browsing session or a full storage quota shouldn't break the app.
  }
}

/** Removes a cached value. Best effort: silently no-ops if unavailable. */
export function clearCache(key: string): void {
  try {
    localStorage.removeItem(STORAGE_PREFIX + key);
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
    const full = STORAGE_PREFIX + prefix;
    const kept = except ? STORAGE_PREFIX + except : null;
    removeKeys(localStorage, (key) => key.startsWith(full) && key !== kept);
  } catch {
    // Best effort only.
  }
}

/**
 * Removes everything the app stored under {@link STORAGE_PREFIX}, in localStorage and
 * sessionStorage alike, and nothing else. What sign-out calls. Each storage is best effort
 * on its own, so one being unavailable doesn't leave the other behind.
 */
export function clearAppStorage(): void {
  try {
    removeKeys(localStorage, (key) => key.startsWith(STORAGE_PREFIX));
  } catch {
    // Best effort only.
  }
  try {
    removeKeys(sessionStorage, (key) => key.startsWith(STORAGE_PREFIX));
  } catch {
    // Best effort only.
  }
}
