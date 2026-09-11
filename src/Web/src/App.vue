<template>
  <RouterView />
  <AppTooltip />
  <OnboardingModal
    v-if="personStore.showOnboarding"
    @submit="completeOnboarding"
    @close="personStore.cancelOnboarding"
  />
</template>

<script lang="ts" setup>
import { watchEffect } from "vue";

import { resolveAccessToken, useAuth, useAuthStore } from "@/auth";
import AppTooltip from "@/components/AppTooltip.vue";
import OnboardingModal from "@/components/OnboardingModal.vue";
import { usePersonStore } from "@/stores/person";
import type { UpdatePersonRequest } from "@/types/person";

const authStore = useAuthStore();
const personStore = usePersonStore();
const { isAuthenticated, isLoading, user } = useAuth();

// The modal stays open on failure with the reason in it, fixable right here.
async function completeOnboarding(
  payload: UpdatePersonRequest,
  done: (error?: string | null) => void,
) {
  try {
    await personStore.completeOnboarding(payload);
    done();
  } catch (error: unknown) {
    console.error("ERROR => Failed to create profile:", error);
    done(error instanceof Error ? error.message : "Failed to create your profile. Try again.");
  }
}

watchEffect(async () => {
  if (isLoading.value || !isAuthenticated.value) return;

  // This syncs the Auth0 token and doesn't depend on the API being up. If it fails,
  // the session itself is bad, so this is the only case where we reset auth state.
  // resolveAccessToken() shares its cache and in-flight request with the router guard
  // and every apiFetch call, so this doesn't trigger its own separate round trip.
  authStore.isLoading = true;
  try {
    await resolveAccessToken();
    if (user.value) authStore.setUser(user.value);
  } catch (error: unknown) {
    console.error("ERROR => Failed to synchronize access token:", error);
    authStore.reset();
    return;
  } finally {
    authStore.isLoading = false;
  }
});
</script>
