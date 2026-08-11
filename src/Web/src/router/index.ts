import { watch } from "vue";
import { createRouter, createWebHistory } from "vue-router";
import type { RouteLocationNormalized } from "vue-router";

import { handleRedirectCallback, resolveAccessToken, signinRedirect, useAuth, useAuthStore } from "@/auth";
import AppLayout from "@/layouts/AppLayout.vue";
import AdminView from "@/views/AdminView.vue";
import ErrorView from "@/views/ErrorView.vue";
import ForbiddenView from "@/views/ForbiddenView.vue";
import HomeView from "@/views/HomeView.vue";
import NotFoundView from "@/views/NotFoundView.vue";
import ProfileView from "@/views/ProfileView.vue";
import UnauthorizedView from "@/views/UnauthorizedView.vue";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: "/",
      component: AppLayout,
      meta: { requiresAuth: true },
      children: [
        { path: "", component: HomeView },
        { path: "profile", component: ProfileView },
        { path: "admin", component: AdminView, meta: { requiresAdmin: true } },
      ],
    },
    {
      // The OIDC provider redirects here with an auth code after login; there's nothing
      // to render, the guard below always redirects away from this route.
      path: "/callback",
      component: HomeView,
      beforeEnter: async () => {
        try {
          await handleRedirectCallback();
        } catch (error: unknown) {
          console.error("ERROR => Failed to handle OIDC redirect callback:", error);
          return "/500";
        }
        return sessionStorage.getItem("auth_redirect") ?? "/";
      },
    },
    { path: "/401", component: UnauthorizedView },
    { path: "/403", component: ForbiddenView },
    { path: "/500", component: ErrorView },
    { path: "/:pathMatch(.*)*", component: NotFoundView },
  ],
});

/**
 * Handles route authentication and admin-permission guarding.
 * Waits for the auth provider's readiness before proceeding.
 */
router.beforeEach(async (to: RouteLocationNormalized) => {
  if (!to.matched.some((r) => r.meta.requiresAuth)) return true;

  const { isAuthenticated, isLoading: isAuthProviderLoading } = useAuth();

  const guardAction = async () => {
    if (isAuthenticated.value) {
      try {
        // Shares the same cached token and in-flight request as App.vue's own sync and
        // every apiFetch call, so a direct or hard navigation doesn't trigger its own
        // separate round trip to the IdP.
        await resolveAccessToken();
      } catch (error: unknown) {
        console.error("ERROR => Failed to resolve access token before route navigation:", error);
      }

      if (to.matched.some((r) => r.meta.requiresAdmin) && !useAuthStore().isAdmin) {
        return "/403";
      }

      return true;
    } else {
      const targetUrl = to.fullPath;
      sessionStorage.setItem("auth_redirect", targetUrl);
      await signinRedirect({ targetUrl });
      return false; // cancel navigation (redirect handled externally)
    }
  };

  if (isAuthProviderLoading.value) {
    return new Promise((resolve) => {
      const unwatch = watch(isAuthProviderLoading, async (v) => {
        if (!v) {
          unwatch();
          resolve(await guardAction());
        }
      });
    });
  }

  return await guardAction();
});

export default router;
