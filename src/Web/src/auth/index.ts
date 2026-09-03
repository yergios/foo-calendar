import { type User as OidcUser, UserManager } from "oidc-client-ts";
import { defineStore } from "pinia";
import { computed, ref } from "vue";

import { appUrl } from "@/lib/appUrl";
import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { clearCache } from "@/lib/browserCache";
import { PERSON_CACHE_KEY } from "@/stores/personCacheKey";
import type { BootstrapAuthConfig } from "@/types/config";
import type { Permission } from "@/types/permission";

// The API reports Auth:Provider=None when there is no identity provider at all: local
// development, or a deployment that opted into running unauthenticated on a trusted
// network (see UnauthenticatedAuthHandler on the API side). In that mode nobody signs in;
// they say who they are and the app believes them, which is what the identity state below
// tracks instead of a token.
const NO_IDP_PROVIDER = "None";

let userManager: UserManager | null = null;
let isDevMode = false;
let devToken = "";

/** True when this deployment has no identity provider, so a visitor says who they are
 * instead of signing in. What the profile form keys off to ask for a username. */
export function usesSelfAssertedIdentity(): boolean {
  return isDevMode;
}

/**
 * Claims a name for this browser. Only meaningful when there is no identity provider; the
 * cookie it sets is what the API resolves every later request as, so it has to land before
 * the profile behind it is read or created.
 */
export async function chooseIdentity(username: string): Promise<void> {
  const res = await fetch(appUrl("/api/identity"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username }),
  });
  if (!res.ok) throw new Error(`API error: ${res.status} ${res.statusText}`);

  // The previous visitor's profile, cached under a key that doesn't mention who it was.
  clearCache(PERSON_CACHE_KEY);
}

/**
 * Gives up the claimed name and reloads, so the app comes back as nobody and asks again.
 * A reload rather than unpicking the state by hand: identity decides which person the API
 * resolves, which permissions the interface offers, and what is cached in localStorage,
 * and re-deriving all of that is a longer list than it looks with a wrong answer that
 * shows up as someone else's calendar row.
 */
export async function forgetIdentity(): Promise<void> {
  // Checked, unlike a fire-and-forget delete would be: reloading regardless would bring the
  // page back as the same person with nothing said, which reads as the button being broken.
  const res = await fetch(appUrl("/api/identity"), { method: "DELETE" });
  if (!res.ok) throw new Error(`API error: ${res.status} ${res.statusText}`);

  clearCache(PERSON_CACHE_KEY);
  window.location.reload();
}

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
  isDevMode = auth.provider === NO_IDP_PROVIDER;

  if (isDevMode) {
    const devUser = auth.devUser;

    // Only in local development, where Auth:DevUser is genuinely who the API resolves
    // unnamed requests as, so prefilling the profile form from it saves typing. On a real
    // unauthenticated deployment it is nobody: every visitor would otherwise open
    // onboarding prefilled as "Dev User" with dev@example.local in the address box, and
    // whoever left it alone would have that saved as where their notifications go.
    devProfile.value = auth.unauthenticatedDeployment
      ? null
      : {
          given_name: devUser?.firstName,
          family_name: devUser?.lastName,
          email: devUser?.email,
        };

    // The API never inspects this token when there is no IdP - it only needs a decodable
    // payload so the existing token/permission plumbing below keeps working. The
    // permissions come from the same Auth:DevUser the API is authenticating as, so the UI
    // can't disagree with what the API will actually allow.
    devToken = `dev.${btoa(
      JSON.stringify({
        // Under the first claim name, matching what the API's handler emits.
        [auth.permissionsClaims[0] ?? "permissions"]: devUser?.permissions ?? [],
        exp: Math.floor(Date.now() / 1000) + 24 * 60 * 60,
      }),
    )}.dev`;

    // Always "authenticated", because there is nothing to authenticate against: a visitor
    // who hasn't named themselves is resolved by the API as an identity that owns no
    // profile, which lands them on the same "set up your profile" form a new hire sees.
    // Claiming a name is part of filling that form in, not a gate in front of it.
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
    scope: auth.scope,
    response_type: "code",
    automaticSilentRenew: false,
    // "audience" isn't part of core OIDC, but several providers (e.g. Auth0) use it to
    // select which API the access token is issued for. Passed through only if set.
    extraQueryParams: auth.audience ? { audience: auth.audience } : undefined,
  });

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

/** Reactive auth state consumed by the router guard and App.vue. */
export function useAuth() {
  return { isAuthenticated, isLoading: isAuthLoading, user: currentProfile };
}

export async function signinRedirect(appState?: { targetUrl?: string }): Promise<void> {
  if (isDevMode || !userManager) return;
  await userManager.signinRedirect(appState ? { state: appState } : undefined);
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

  /** The signed-in user's address as the identity provider stated it, for prefilling the
   * profile form. Null when there is no provider, or when it sent no email claim. */
  const getEmail = (): string | null => {
    const claim = currentUser.value?.email;
    return typeof claim === "string" && claim.length > 0 ? claim : null;
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
    getEmail,
    getCachedToken,
    reset,
  };
});

// Shared across every caller (the router guard, App.vue's own sync, and every apiFetch
// call): serve the cached token when it's still fresh, and let concurrent callers share
// one in-flight request instead of each asking the IdP for their own.
let pendingToken: Promise<string> | null = null;

export async function resolveAccessToken(): Promise<string> {
  const authStore = useAuthStore();

  if (isDevMode) return devToken;

  const cached = authStore.getCachedToken();
  if (cached) return cached;

  pendingToken ??= (async () => {
    let user = oidcUser.value;
    if (!user || user.expired) {
      user = await userManager!.signinSilent();
      oidcUser.value = user;
    }

    const token = user?.access_token;
    if (!token) throw new Error("The OIDC provider did not return an access token.");

    authStore.setToken(token);
    return token;
  })().finally(() => {
    pendingToken = null;
  });

  return pendingToken;
}
