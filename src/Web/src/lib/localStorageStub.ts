/** Test-only: Node has no localStorage, and a Map-backed stand-in is all the browser
 * cache helpers need. Installed onto globalThis so the module under test picks it up;
 * call it in a beforeEach so each test starts from an empty store. Never import this from
 * production code. */
export function stubLocalStorage() {
  const store = new Map<string, string>();
  const stub = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => void store.set(key, value),
    removeItem: (key: string) => void store.delete(key),
    key: (i: number) => [...store.keys()][i] ?? null,
    get length() {
      return store.size;
    },
  };
  Object.defineProperty(globalThis, "localStorage", { value: stub, configurable: true });
}
