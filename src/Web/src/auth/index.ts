import {
  ErrorResponse,
  type User as OidcUser,
  UserManager,
  WebStorageStateStore,
} from "oidc-client-ts";
import { defineStore } from "pinia";
import { computed, ref } from "vue";

import { appUrl, basePath } from "@/lib/appUrl";
import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { STORAGE_PREFIX, clearAppStorage, stopCacheWrites } from "@/lib/browserCache";
import type { BootstrapAuthConfig } from "@/types/config";
import type { Permission } from "@/types/permission";

// The API reports Auth:Provider=None when SSO is bypassed entirely, which Program.cs
// refuses outside Development. It lets the app run with zero external IdP dependency
// when no real OIDC tenant is available (see DevAuthHandler on the API side).
const DEV_PROVIDER = "None";

// The library's default is "oidc.". Under the app's prefix instead, its session and any
// sign-in in progress go with everything else when sign-out clears the app's storage, and
// that clearing never has to touch "oidc." keys another app on this origin may own.
const OIDC_PREFIX = `${STORAGE_PREFIX}oidc.`;

/** Where to go back to once the identity provider returns the browser. Session storage: it
 * only has to survive that one round trip. Under the app's prefix like everything else it
 * stores, so sign-out removes it. */
export const AUTH_REDIRECT_KEY = `${STORAGE_PREFIX}auth_redirect`;

let userManager: UserManager | null = null;
let isDevMode = false;
let devToken = "";
// Set when sign-out starts. Sign-out ends by leaving the page, so it is never unset.
let signingOut = false;

const oidcUser = ref<OidcUser | null>(null);
const devProfile = ref<Record<string, unknown> | null>(null);
const isAuthenticated = ref(false);
const isAuthLoading = ref(true);

const currentProfile = computed(() =>
  isDevMode ? devProfile.value : (oidcUser.value?.profile ?? null),
);

/**
 * Wires up authentication from the config the API served. Called once by main.ts before
 * the app mounts, so nothing in this module works until it has run.
 */
export function initAuth(auth: BootstrapAuthConfig): void {
  isDevMode = auth.provider === DEV_PROVIDER;

  if (isDevMode) {
    const devUser = auth.devUser;
    devProfile.value = {
      given_name: devUser?.firstName,
      family_name: devUser?.lastName,
      email: devUser?.email,
    };

    // The API's DevAuthHandler never inspects the token in dev mode - this only needs a
    // decodable payload so the existing token/permission plumbing below keeps working.
    // The permissions come from the same Auth:DevUser the API is authenticating as, so
    // the UI can't disagree with what the API will actually allow.
    devToken = `dev.${btoa(
      JSON.stringify({
        // Under the first claim name, matching what DevAuthHandler emits.
        [auth.permissionsClaims[0] ?? "permissions"]: devUser?.permissions ?? [],
        exp: Math.floor(Date.now() / 1000) + 24 * 60 * 60,
      }),
    )}.dev`;

    isAuthenticated.value = true;
    isAuthLoading.value = false;
    return;
  }

  userManager = new UserManager({
    authority: auth.authority,
    client_id: auth.clientId,
    // Relative by default, resolved against wherever the app is actually being served
    // from - hostname and sub-path both - so one build works for any deployment. An
    // absolute URL is passed through untouched.
    redirect_uri: appUrl(auth.redirectUri),
    post_logout_redirect_uri: appUrl(auth.postLogoutRedirectUri),
    scope: auth.scope,
    response_type: "code",
    userStore: new WebStorageStateStore({ prefix: OIDC_PREFIX, store: sessionStorage }),
    stateStore: new WebStorageStateStore({ prefix: OIDC_PREFIX, store: localStorage }),
    automaticSilentRenew: false,
    // "audience" isn't part of core OIDC, but several providers (e.g. Auth0) use it to
    // select which API the access token is issued for. Passed through only if set.
    extraQueryParams: auth.audience ? { audience: auth.audience } : undefined,
  });

  moveLegacySession(auth);

  userManager.events.addUserLoaded((u) => {
    oidcUser.value = u;
    isAuthenticated.value = true;
  });
  userManager.events.addUserUnloaded(() => {
    oidcUser.value = null;
    isAuthenticated.value = false;
  });

  userManager
    .getUser()
    .then((u) => {
      oidcUser.value = u && !u.expired ? u : null;
      isAuthenticated.value = oidcUser.value !== null;
    })
    .finally(() => {
      isAuthLoading.value = false;
    });
}

/**
 * Moves a session the library stored under its default "oidc." prefix, before it was given
 * the app's, so the upgrade signs nobody out and sign-out still finds it. The key is the
 * library's own (UserManager's user store key); if that ever changes, the worst case is
 * signing in again. Sessions live in sessionStorage, which goes when the tab closes, so
 * this can go a release after it shipped.
 */
function moveLegacySession(auth: BootstrapAuthConfig): void {
  try {
    const key = `user:${auth.authority}:${auth.clientId}`;
    const legacy = sessionStorage.getItem(`oidc.${key}`);
    if (legacy === null) return;
    sessionStorage.setItem(OIDC_PREFIX + key, legacy);
    sessionStorage.removeItem(`oidc.${key}`);
  } catch {
    // Best effort only: without it, the member signs in again.
  }
}

/** Reactive auth state consumed by the router guard and App.vue. */
export function useAuth() {
  return { isAuthenticated, isLoading: isAuthLoading, user: currentProfile };
}

/** Leaves for the identity provider's sign-in, to come back to `targetUrl`, a path within
 * the app such as "/admin/people". */
export async function signinRedirect(appState?: { targetUrl?: string }): Promise<void> {
  if (isDevMode || !userManager || signingOut) return;
  if (appState?.targetUrl) sessionStorage.setItem(AUTH_REDIRECT_KEY, appState.targetUrl);
  await userManager.signinRedirect(appState ? { state: appState } : undefined);
}

/**
 * Ends the session here and at the identity provider, and removes everything the app
 * stored in the browser. Interface preferences go too: on a shared computer they would say
 * who used it last, and they are cheap to set again.
 */
export async function signOut(): Promise<void> {
  // From here until the page is left, nothing may write to storage or fetch a token. A
  // revalidation already in flight would otherwise put back the calendar this clears, and
  // a request finding no user once it is removed would start signing in again, which the
  // provider's own session, wherever it is still alive, would answer with a new one.
  signingOut = true;
  stopCacheWrites();
  useAuthStore().reset();

  if (isDevMode || !userManager) {
    // The dev identity has no session anywhere to end; starting over from empty storage is
    // all signing out can mean here.
    clearAppStorage();
    window.location.reload();
    return;
  }

  // Read before the storage holding it is cleared: the provider takes the id token as the
  // hint of whose session to end, and some refuse to end one without it.
  const user = await userManager.getUser();
  // The library's session and sign-in state are under the app's prefix, so this removes
  // them too; removeUser is still what tells the library, and its listeners, it is gone.
  clearAppStorage();
  await userManager.removeUser();
  const endSession = await hasEndSessionEndpoint(userManager);

  // Again, last thing before leaving: anything that was already past the checks above when
  // sign-out started, such as a token renewal under way, may have written since.
  clearAppStorage();

  if (endSession) {
    await userManager.signoutRedirect({ id_token_hint: user?.id_token });
    return;
  }

  // A provider without an end-session endpoint keeps its own session, so the most this
  // can do is drop the app's and go back through sign-in, which that session may answer.
  window.location.assign(userManager.settings.post_logout_redirect_uri ?? appUrl("/"));
}

// An unreachable discovery document counts as no endpoint: signing out must still work
// when the provider is down, and the local half of it is all that is possible then.
async function hasEndSessionEndpoint(manager: UserManager): Promise<boolean> {
  try {
    return Boolean(await manager.metadataService.getEndSessionEndpoint());
  } catch {
    return false;
  }
}

/** Called from the /callback route after the IdP redirects back with an auth code. */
export async function handleRedirectCallback(): Promise<void> {
  if (isDevMode || !userManager) return;
  oidcUser.value = await userManager.signinRedirectCallback();
  isAuthenticated.value = true;
}

function decodeJwtPayload(token: string): Record<string, unknown> {
  try {
    const payloadPart = token.split(".")[1];
    if (!payloadPart) return {};
    return JSON.parse(atob(payloadPart));
  } catch {
    return {};
  }
}

// Which claims carry permissions is per-IdP config, and providers are inconsistent about
// whether a single value arrives as a string or a one-element array. Normalize both, and
// collect across every configured claim.
function readPermissions(payload: Record<string, unknown>, claims: string[]): string[] {
  return claims.flatMap((claim) => {
    const value = payload[claim];
    if (Array.isArray(value)) return value as string[];
    if (typeof value === "string") return [value];
    return [];
  });
}

// Keeps a request from handing out a token that's about to expire mid-flight.
const TOKEN_EXPIRY_BUFFER_MS = 10_000;

export const useAuthStore = defineStore("auth", () => {
  const isLoading = ref<boolean>(false);
  const currentUser = ref<Record<string, unknown> | null>(isDevMode ? devProfile.value : null);
  const permissions = ref<string[]>([]);
  const accessToken = ref<string | null>(null);
  const tokenExpiresAt = ref(0);

  /** Marks the user as authenticated and records the permissions and expiry carried by their token. */
  const setToken = (newToken: string): void => {
    accessToken.value = newToken;

    const payload = decodeJwtPayload(newToken);
    permissions.value = readPermissions(payload, bootstrapConfig().auth.permissionsClaims);
    tokenExpiresAt.value = typeof payload.exp === "number" ? payload.exp * 1000 : 0;
  };

  /** Returns the cached token if it's still fresh, or null if you need to ask the IdP for a new one. */
  const getCachedToken = (): string | null => {
    if (!accessToken.value) return null;
    if (Date.now() > tokenExpiresAt.value - TOKEN_EXPIRY_BUFFER_MS) return null;
    return accessToken.value;
  };

  const setUser = (newUser: Record<string, unknown>): void => {
    currentUser.value = newUser;
  };

  const getFullName = (): string | null => {
    const parts = [currentUser.value?.given_name, currentUser.value?.family_name].filter(Boolean);
    return parts.length > 0 ? parts.join(" ") : null;
  };

  // Case-insensitive to match the API's own check: group identifiers are often GUIDs
  // whose casing differs between a provider's dashboard and its tokens.
  const holdsAnyOf = (accepted: string[]): boolean =>
    permissions.value.some((held) =>
      accepted.some((value) => value.toLowerCase() === held.toLowerCase()),
    );

  /** Whether the signed-in user holds one named permission. Only ever decides what the
   * interface offers; the API enforces the same map on every request regardless. */
  const can = (permission: Permission): boolean =>
    holdsAnyOf(bootstrapConfig().auth.permissions[permission] ?? []);

  /** Whether they hold anything at all, which is what the admin area amounts to: a page
   * of sections, each behind its own permission. */
  const canAnything = computed(() =>
    Object.keys(bootstrapConfig().auth.permissions).some((permission) =>
      holdsAnyOf(bootstrapConfig().auth.permissions[permission] ?? []),
    ),
  );

  /** Resets auth state back to its initial values. */
  const reset = (): void => {
    isLoading.value = false;
    currentUser.value = null;
    permissions.value = [];
    accessToken.value = null;
    tokenExpiresAt.value = 0;
  };

  if (isDevMode) setToken(devToken);

  return {
    isLoading,
    can,
    canAnything,
    setToken,
    setUser,
    getFullName,
    getCachedToken,
    reset,
  };
});

// Shared across every caller (the router guard, App.vue's own sync, and every apiFetch
// call): serve the cached token when it's still fresh, and let concurrent callers share
// one in-flight request instead of each asking the IdP for their own.
let inFlightToken: Promise<string> | null = null;

export async function resolveAccessToken(): Promise<string> {
  // Never settles: the page is about to be left, a token now could only come from a renewal
  // or sign-in that sign-out is undoing, and an error would only flash up on the way out.
  if (signingOut) return new Promise<string>(() => {});

  const authStore = useAuthStore();

  if (isDevMode) return devToken;

  const cached = authStore.getCachedToken();
  if (cached) return cached;

  inFlightToken ??= (async () => {
    let user = oidcUser.value;
    if (!user || user.expired) {
      user = await renew(user);
      oidcUser.value = user;
    }

    const token = user?.access_token;
    if (!token) throw new Error("The OIDC provider did not return an access token.");

    authStore.setToken(token);
    return token;
  })().finally(() => {
    inFlightToken = null;
  });

  return inFlightToken;
}

/**
 * A fresh session for one that has expired, by refresh token only (docs/decisions/0017).
 * The library's other way, a hidden frame on the provider, cannot work under the app's
 * Content-Security-Policy: it would only time out, ten seconds later. With no refresh token,
 * or one the provider refuses, the session is over and the member signs in again, coming
 * back to the page they were on. That leaves the page, so it never settles.
 *
 * A refresh that fails for any other reason, such as the network, is thrown to the caller
 * as before: the provider did not say the session is over.
 */
async function renew(user: OidcUser | null): Promise<OidcUser> {
  if (user?.refresh_token) {
    try {
      const renewed = await userManager!.signinSilent();
      if (renewed) return renewed;
    } catch (error: unknown) {
      if (!(error instanceof ErrorResponse)) throw error;
    }
  }

  const here = `/${window.location.pathname.slice(basePath.length)}${window.location.search}${window.location.hash}`;
  await signinRedirect({ targetUrl: here });
  return new Promise<OidcUser>(() => {});
}
