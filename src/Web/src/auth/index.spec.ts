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
  const location = {
    assign: vi.fn(),
    reload: vi.fn(),
    pathname: "/team/admin/people",
    search: "?page=2",
    hash: "",
  };
  Object.assign(globalThis, {
    document: { baseURI: "https://calendar.example/team/", createElement: () => ({}) },
    window: { location },
  });
  return {
    location,
    calls: [] as string[],
    endSessionEndpoint: undefined as string | undefined,
    user: { id_token: "the-id-token", expired: false, profile: {} } as Record<string, unknown>,
    // What renewing with a refresh token does: hands back a new session by default.
    renew: (): Promise<unknown> =>
      Promise.resolve({ ...browser.user, expired: false, access_token: "a-fresh-token" }),
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
      return browser.renew();
    }

    signinRedirect(args: unknown) {
      browser.calls.push(`signinRedirect ${JSON.stringify(args)}`);
      return Promise.resolve();
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

// Renewal goes through refresh tokens only (docs/decisions/0017): a hidden frame on the
// provider cannot work under the app's Content-Security-Policy and would only time out.
describe("resolveAccessToken", () => {
  // A fresh copy of the module for each: signing out, which the tests above do, is final
  // for the module that did it.
  let auth: typeof import("@/auth");
  // From the same copy of the library the module was loaded with, since it checks the
  // class of what a renewal throws.
  let ErrorResponse: typeof import("oidc-client-ts").ErrorResponse;

  /** Settles within a few turns of the event loop, or reports that it did not. */
  async function outcome(promise: Promise<string>): Promise<string> {
    const result = promise.then(
      (token) => `token ${token}`,
      (error: unknown) => `error ${(error as Error).message}`,
    );
    const pending = new Promise<string>((resolve) => setTimeout(() => resolve("pending"), 20));
    return Promise.race([result, pending]);
  }

  /** The app signed in with a session that has since expired. */
  async function expiredSession(session: Record<string, unknown>): Promise<void> {
    browser.user = { id_token: "the-id-token", expired: false, profile: {}, ...session };
    auth.initAuth(oidcConfig);
    // Let the session load, then let it run out.
    await new Promise((resolve) => setTimeout(resolve, 0));
    browser.user.expired = true;
  }

  beforeEach(async () => {
    vi.resetModules();
    auth = await import("@/auth");
    ({ ErrorResponse } = await import("oidc-client-ts"));
    setActivePinia(createPinia());
    stubStorage("localStorage");
    stubStorage("sessionStorage");
    browser.calls.length = 0;
    browser.renew = () =>
      Promise.resolve({ ...browser.user, expired: false, access_token: "a-fresh-token" });
    // The bootstrap config is what the store reads the permissions claim from.
    Object.assign(window, {
      __bootstrap: { orgName: "", supportUrl: "", issueUrl: "", auth: oidcConfig },
    });
    await (await import("@/lib/bootstrapConfig")).loadBootstrapConfig();
  });

  it("renews an expired session with its refresh token", async () => {
    await expiredSession({ refresh_token: "a-refresh-token" });

    expect(await outcome(auth.resolveAccessToken())).toBe("token a-fresh-token");
    expect(browser.calls).toContain("signinSilent");
  });

  it("signs in again, back to the same page, when the session has no refresh token", async () => {
    await expiredSession({});

    expect(await outcome(auth.resolveAccessToken())).toBe("pending");
    expect(browser.calls).not.toContain("signinSilent");
    expect(browser.calls).toContain(
      `signinRedirect ${JSON.stringify({ state: { targetUrl: "/admin/people?page=2" } })}`,
    );
    expect(sessionStorage.getItem(auth.AUTH_REDIRECT_KEY)).toBe("/admin/people?page=2");
  });

  it("signs in again when the provider refuses the refresh token", async () => {
    await expiredSession({ refresh_token: "a-revoked-token" });
    browser.renew = () => Promise.reject(new ErrorResponse({ error: "invalid_grant" }));

    expect(await outcome(auth.resolveAccessToken())).toBe("pending");
    expect(browser.calls).toContainEqual(expect.stringMatching(/^signinRedirect /));
  });

  it("reports a refresh that failed without the provider refusing it", async () => {
    await expiredSession({ refresh_token: "a-refresh-token" });
    browser.renew = () => Promise.reject(new TypeError("Failed to fetch"));

    expect(await outcome(auth.resolveAccessToken())).toBe("error Failed to fetch");
    expect(browser.calls).not.toContainEqual(expect.stringMatching(/^signinRedirect /));
  });
});
