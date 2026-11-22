import { beforeEach, describe, expect, it } from "vitest";

import {
  STORAGE_PREFIX,
  clearAppStorage,
  clearCacheGroup,
  readCache,
  writeCache,
} from "./browserCache";
import { stubLocalStorage, stubStorage } from "./localStorageStub";

describe("browserCache", () => {
  beforeEach(stubLocalStorage);

  it("[UI-7] round-trips values through JSON", () => {
    writeCache("k", { a: 1 });
    expect(readCache<{ a: number }>("k")).toEqual({ a: 1 });
    expect(readCache("missing")).toBeNull();
  });

  it("[UI-8] clearCacheGroup removes every key under the prefix and nothing else", () => {
    writeCache("absences:2025.v1", []);
    writeCache("absences:2026.v0", []);
    writeCache("holidays:2026.v1", []);
    clearCacheGroup("absences:");
    expect(readCache("absences:2025.v1")).toBeNull();
    expect(readCache("absences:2026.v0")).toBeNull();
    expect(readCache("holidays:2026.v1")).toEqual([]);
  });

  it("[UI-8] clearCacheGroup spares the excepted key", () => {
    writeCache("people.v0", 1);
    writeCache("people.v1", 2);
    clearCacheGroup("people.", "people.v1");
    expect(readCache("people.v0")).toBeNull();
    expect(readCache("people.v1")).toBe(2);
  });

  it("[UI-9] writes every key under the app's prefix", () => {
    writeCache("teamSortMode", "mine");
    expect(localStorage.key(0)).toBe(`${STORAGE_PREFIX}teamSortMode`);
  });

  it("[UI-7] never throws when localStorage is unavailable", () => {
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

describe("clearAppStorage", () => {
  beforeEach(() => {
    stubStorage("localStorage");
    stubStorage("sessionStorage");
  });

  it("[UI-9] [AUTH-8] removes the app's keys from both storages and leaves other apps' alone", () => {
    writeCache("absences:2026.v2", []);
    writeCache("teamCustomOrder", [3, 1, 2]);
    sessionStorage.setItem(`${STORAGE_PREFIX}auth_redirect`, "/admin");
    localStorage.setItem("another-app:theme", "dark");
    sessionStorage.setItem("oidc.user:https://idp.example:client", "{}");

    clearAppStorage();

    expect(localStorage.length).toBe(1);
    expect(localStorage.getItem("another-app:theme")).toBe("dark");
    expect(sessionStorage.length).toBe(1);
    expect(sessionStorage.getItem("oidc.user:https://idp.example:client")).toBe("{}");
  });

  it("[UI-9] still clears sessionStorage when localStorage is unavailable", () => {
    sessionStorage.setItem(`${STORAGE_PREFIX}auth_redirect`, "/admin");
    Object.defineProperty(globalThis, "localStorage", {
      get() {
        throw new Error("blocked");
      },
      configurable: true,
    });

    expect(() => clearAppStorage()).not.toThrow();
    expect(sessionStorage.length).toBe(0);
  });
});
