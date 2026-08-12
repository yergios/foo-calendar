import { UserManager, type User as OidcUser } from "oidc-client-ts";
import { defineStore } from "pinia";
import { computed, ref } from "vue";

// VITE_AUTH_MODE=dev is only ever set in .env.development, so this can't accidentally
// skip real SSO once deployed. It lets the app run with zero external IdP dependency
// when no real OIDC tenant is available (see DevAuthHandler on the API side).
const isDevMode = import.meta.env.VITE_AUTH_MODE === "dev";

const userManager = isDevMode
  ? null
  : new UserManager({
      authority: import.meta.env.VITE_OIDC_AUTHORITY,
      client_id: import.meta.env.VITE_OIDC_CLIENT_ID,
      redirect_uri: import.meta.env.VITE_OIDC_REDIRECT_URI,
      scope: import.meta.env.VITE_OIDC_SCOPE,
      response_type: "code",
      automaticSilentRenew: false,
      // "audience" isn't part of core OIDC, but several providers (e.g. Auth0) use it to
      // select which API the access token is issued for. Passed through only if set.
      extraQueryParams: import.meta.env.VITE_OIDC_AUDIENCE
        ? { audience: import.meta.env.VITE_OIDC_AUDIENCE }
        : undefined,
    });

const DEV_PROFILE = {
  given_name: "Dev",
  family_name: "User",
  email: "dev@example.local",
};

// The API's DevAuthHandler never inspects the token in dev mode - this only needs a
// decodable payload so the existing token/permission plumbing below keeps working.
const DEV_TOKEN = `dev.${btoa(
  JSON.stringify({
    permissions: [import.meta.env.VITE_ADMIN_PERMISSION].filter(Boolean),
    exp: Math.floor(Date.now() / 1000) + 24 * 60 * 60,
  }),
)}.dev`;

const oidcUser = ref<OidcUser | null>(null);
const isAuthenticated = ref(isDevMode);
const isAuthLoading = ref(!isDevMode);

const currentProfile = computed(() => (isDevMode ? DEV_PROFILE : (oidcUser.value?.profile ?? null)));

if (!isDevMode && userManager) {
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

// Keeps a request from handing out a token that's about to expire mid-flight.
const TOKEN_EXPIRY_BUFFER_MS = 10_000;

export const useAuthStore = defineStore("auth", () => {
  const isLoading = ref<boolean>(false);
  const currentUser = ref<Record<string, unknown> | null>(isDevMode ? DEV_PROFILE : null);
  const permissions = ref<string[]>([]);
  const accessToken = ref<string | null>(null);
  const tokenExpiresAt = ref(0);

  /** Marks the user as authenticated and records the permissions and expiry carried by their token. */
  const setToken = (newToken: string): void => {
    accessToken.value = newToken;

    const payload = decodeJwtPayload(newToken);
    permissions.value = (payload.permissions as string[]) ?? [];
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

  const hasPermission = (permission: string): boolean => {
    return permissions.value.includes(permission);
  };

  // VITE_BYPASS_ADMIN_PERMISSION is only ever set in .env.development, so this can't
  // accidentally bypass the check once deployed.
  const isAdmin = computed(
    () =>
      import.meta.env.VITE_BYPASS_ADMIN_PERMISSION === "true" ||
      hasPermission(import.meta.env.VITE_ADMIN_PERMISSION ?? ""),
  );

  /** Resets auth state back to its initial values. */
  const reset = (): void => {
    isLoading.value = false;
    currentUser.value = null;
    permissions.value = [];
    accessToken.value = null;
    tokenExpiresAt.value = 0;
  };

  if (isDevMode) setToken(DEV_TOKEN);

  return {
    isLoading,
    isAdmin,
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
let pendingToken: Promise<string> | null = null;

export async function resolveAccessToken(): Promise<string> {
  const authStore = useAuthStore();

  if (isDevMode) return DEV_TOKEN;

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
