import { beforeEach, describe, expect, it } from "vitest";

import { clearCacheGroup, readCache, writeCache } from "./browserCache";
import { stubLocalStorage } from "./localStorageStub";

describe("browserCache", () => {
  beforeEach(stubLocalStorage);

  it("round-trips values through JSON", () => {
    writeCache("k", { a: 1 });
    expect(readCache<{ a: number }>("k")).toEqual({ a: 1 });
    expect(readCache("missing")).toBeNull();
  });

  it("clearCacheGroup removes every key under the prefix and nothing else", () => {
    writeCache("absences:2025.v1", []);
    writeCache("absences:2026.v0", []);
    writeCache("holidays:2026.v1", []);
    clearCacheGroup("absences:");
    expect(readCache("absences:2025.v1")).toBeNull();
    expect(readCache("absences:2026.v0")).toBeNull();
    expect(readCache("holidays:2026.v1")).toEqual([]);
  });

  it("clearCacheGroup spares the excepted key", () => {
    writeCache("people.v0", 1);
    writeCache("people.v1", 2);
    clearCacheGroup("people.", "people.v1");
    expect(readCache("people.v0")).toBeNull();
    expect(readCache("people.v1")).toBe(2);
  });

  it("never throws when localStorage is unavailable", () => {
    Object.defineProperty(globalThis, "localStorage", {
      get() {
        throw new Error("blocked");
      },
      configurable: true,
    });
    expect(readCache("k")).toBeNull();
    expect(() => writeCache("k", 1)).not.toThrow();
    expect(() => clearCacheGroup("k")).not.toThrow();
  });
});
