import type { WebStorageStateStore } from "oidc-client-ts";
import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { STORAGE_PREFIX, readCache, writeCache } from "@/lib/browserCache";
import { stubStorage } from "@/lib/localStorageStub";
import type { BootstrapAuthConfig } from "@/types/config";

// Node has no document or window. appUrl reads document.baseURI as its module loads, and
// Vue's DOM runtime creates an element as it loads, so these have to exist before the
// imports below run, which is what vi.hoisted is for.
const browser = vi.hoisted(() => {
  const location = { assign: vi.fn(), reload: vi.fn() };
  Object.assign(globalThis, {
    document: { baseURI: "https://calendar.example/team/", createElement: () => ({}) },
    window: { location },
  });
  return {
    location,
    calls: [] as string[],
    endSessionEndpoint: undefined as string | undefined,
    user: { id_token: "the-id-token", expired: false, profile: {} },
    settings: {} as Record<string, unknown>,
    // Runs while sign-out waits on the provider's metadata, the window in which a request
    // already in flight could still finish.
    whileSigningOut: () => {},
  };
});

// Stands in for oidc-client-ts's UserManager, recording the order things happened in. The
// rest of the library is the real one, so its storage is what the app configures.
vi.mock("oidc-client-ts", async (importOriginal) => ({
  ...(await importOriginal<typeof import("oidc-client-ts")>()),
  UserManager: class {
    settings: Record<string, unknown>;
    events = { addUserLoaded: vi.fn(), addUserUnloaded: vi.fn() };
    metadataService = {
      getEndSessionEndpoint: async () => {
        browser.whileSigningOut();
        return browser.endSessionEndpoint;
      },
    };

    constructor(settings: Record<string, unknown>) {
      this.settings = settings;
      browser.settings = settings;
    }

    getUser() {
      browser.calls.push("getUser");
      return Promise.resolve(browser.user);
    }

    signinSilent() {
      browser.calls.push("signinSilent");
      return Promise.resolve({ ...browser.user, access_token: "a-fresh-token" });
    }

    removeUser() {
      browser.calls.push("removeUser");
      return Promise.resolve();
    }

    signoutRedirect(args: unknown) {
      browser.calls.push(`signoutRedirect ${JSON.stringify(args)}`);
      browser.calls.push(`stored ${localStorage.length + sessionStorage.length}`);
      return Promise.resolve();
    }
  },
}));

const { initAuth, resolveAccessToken, signOut } = await import("@/auth");
const { loadBootstrapConfig } = await import("@/lib/bootstrapConfig");

const oidcConfig: BootstrapAuthConfig = {
  provider: "Oidc",
  authority: "https://idp.example",
  clientId: "foo-calendar",
  audience: "",
  scope: "openid",
  redirectUri: "/callback",
  postLogoutRedirectUri: "/",
  permissions: {},
  permissionsClaims: ["permissions"],
  devUser: null,
};

describe("signOut", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    stubStorage("localStorage");
    stubStorage("sessionStorage");
    browser.calls.length = 0;
    browser.location.assign.mockClear();
    browser.location.reload.mockClear();
    browser.endSessionEndpoint = undefined;
    browser.user = { id_token: "the-id-token", expired: false, profile: {} };
    browser.whileSigningOut = () => {};

    localStorage.setItem(`${STORAGE_PREFIX}teamSortMode`, '"mine"');
    sessionStorage.setItem(`${STORAGE_PREFIX}auth_redirect`, "/admin");
  });

  it("[AUTH-8] [UI-9] ends the provider's session with the id token as hint, after clearing storage", async () => {
    browser.endSessionEndpoint = "https://idp.example/logout";
    initAuth(oidcConfig);

    await signOut();

    expect(browser.calls.slice(-4)).toEqual([
      "getUser",
      "removeUser",
      `signoutRedirect ${JSON.stringify({ id_token_hint: "the-id-token" })}`,
      "stored 0",
    ]);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(browser.location.assign).not.toHaveBeenCalled();
  });

  it("[AUTH-8] signs out locally and goes back to sign in when the provider has no end-session endpoint", async () => {
    initAuth(oidcConfig);

    await signOut();

    expect(browser.calls).not.toContainEqual(expect.stringMatching(/^signoutRedirect/));
    expect(browser.calls).toContain("removeUser");
    expect(browser.location.assign).toHaveBeenCalledWith("https://calendar.example/team/");
    expect(localStorage.length).toBe(0);
  });

  it("[AUTH-8] [UI-9] clears storage and reloads under the development identity", async () => {
    const devConfig = { ...oidcConfig, provider: "None" };
    // The dev identity's token is read through the bootstrap config, as the app loads it.
    Object.assign(window, {
      __bootstrap: { orgName: "", supportUrl: "", issueUrl: "", auth: devConfig },
    });
    await loadBootstrapConfig();
    initAuth(devConfig);

    await signOut();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(browser.location.reload).toHaveBeenCalledOnce();
  });
  it("[AUTH-8] [UI-9] writes nothing to the cache once it has started", async () => {
    browser.endSessionEndpoint = "https://idp.example/logout";
    initAuth(oidcConfig);
    // A revalidation that was in flight when sign-out started, landing in the middle of it.
    browser.whileSigningOut = () => writeCache("absences:2026.v2", [{ id: 1 }]);

    await signOut();
    writeCache("teamSortMode", "name");

    expect(readCache("absences:2026.v2")).toBeNull();
    expect(readCache("teamSortMode")).toBeNull();
    expect(localStorage.length).toBe(0);
  });

  it("[AUTH-8] [UI-9] clears again before leaving what was stored while it ran", async () => {
    browser.endSessionEndpoint = "https://idp.example/logout";
    initAuth(oidcConfig);
    // A silent renew already under way stores its session, and the router its return page,
    // past the point where sign-out first cleared.
    browser.whileSigningOut = () => {
      sessionStorage.setItem(`${STORAGE_PREFIX}oidc.user:https://idp.example:foo-calendar`, "{}");
      sessionStorage.setItem(`${STORAGE_PREFIX}auth_redirect`, "/");
    };

    await signOut();

    expect(browser.calls.at(-1)).toBe("stored 0");
  });

  it("[AUTH-8] fetches no token once it has started, so nothing signs in again silently", async () => {
    // No usable session in memory, which is what a request would otherwise renew silently.
    browser.user = { ...browser.user, expired: true };
    initAuth(oidcConfig);
    await Promise.resolve();

    const signingOut = signOut();
    let settled = false;
    void resolveAccessToken().finally(() => (settled = true));
    await signingOut;
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(browser.calls).not.toContain("signinSilent");
    expect(settled).toBe(false);
  });

  it("[UI-9] keeps the OIDC library's session and sign-in state under the app's prefix", async () => {
    browser.endSessionEndpoint = "https://idp.example/logout";
    initAuth(oidcConfig);
    const { stateStore, userStore } = browser.settings as Record<string, WebStorageStateStore>;
    // What the library writes when a sign-in starts and when it completes.
    await stateStore!.set("a-sign-in-in-progress", "{}");
    await userStore!.set("user:https://idp.example:foo-calendar", "{}");

    expect(localStorage.getItem(`${STORAGE_PREFIX}oidc.a-sign-in-in-progress`)).toBe("{}");
    expect(
      sessionStorage.getItem(`${STORAGE_PREFIX}oidc.user:https://idp.example:foo-calendar`),
    ).toBe("{}");

    await signOut();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it("[UI-9] moves a session stored under the library's default prefix to the app's", () => {
    const key = "user:https://idp.example:foo-calendar";
    sessionStorage.setItem(`oidc.${key}`, '{"id_token":"from-before"}');

    initAuth(oidcConfig);

    expect(sessionStorage.getItem(`oidc.${key}`)).toBeNull();
    expect(sessionStorage.getItem(`${STORAGE_PREFIX}oidc.${key}`)).toBe(
      '{"id_token":"from-before"}',
    );
  });
});
