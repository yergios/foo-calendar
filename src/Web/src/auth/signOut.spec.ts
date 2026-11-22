import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { STORAGE_PREFIX } from "@/lib/browserCache";
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
  return { location, calls: [] as string[], endSessionEndpoint: undefined as string | undefined };
});

// Stands in for oidc-client-ts's UserManager, recording the order things happened in.
vi.mock("oidc-client-ts", () => ({
  UserManager: class {
    settings: Record<string, unknown>;
    events = { addUserLoaded: vi.fn(), addUserUnloaded: vi.fn() };
    metadataService = {
      getEndSessionEndpoint: () => Promise.resolve(browser.endSessionEndpoint),
    };

    constructor(settings: Record<string, unknown>) {
      this.settings = settings;
    }

    getUser() {
      browser.calls.push("getUser");
      return Promise.resolve({ id_token: "the-id-token", expired: false, profile: {} });
    }

    removeUser() {
      browser.calls.push("removeUser");
      return Promise.resolve();
    }

    clearStaleState() {
      return Promise.resolve();
    }

    signoutRedirect(args: unknown) {
      browser.calls.push(`signoutRedirect ${JSON.stringify(args)}`);
      return Promise.resolve();
    }
  },
}));

const { initAuth, signOut } = await import("@/auth");
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

    localStorage.setItem(`${STORAGE_PREFIX}teamSortMode`, '"mine"');
    sessionStorage.setItem(`${STORAGE_PREFIX}auth_redirect`, "/admin");
  });

  it("[AUTH-8] [UI-9] ends the provider's session with the id token as hint, after clearing storage", async () => {
    browser.endSessionEndpoint = "https://idp.example/logout";
    initAuth(oidcConfig);

    await signOut();

    expect(browser.calls.slice(-3)).toEqual([
      "getUser",
      "removeUser",
      `signoutRedirect ${JSON.stringify({ id_token_hint: "the-id-token" })}`,
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
});
