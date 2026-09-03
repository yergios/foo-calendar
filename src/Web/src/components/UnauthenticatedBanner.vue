<template>
  <!-- Only on a real deployment running without authentication, never in local
       development, which is the same provider setting but nobody's security problem. The
       API decides which of the two this is (see BootstrapAuthConfig.unauthenticatedDeployment);
       it isn't dismissible, because a deployment that has quietly lost its identity
       provider should keep saying so. -->
  <div
    v-if="unauthenticated"
    class="shrink-0 bg-holiday border-b border-border-strong px-6 py-1.5 text-center text-xs text-ink"
  >
    <strong class="font-semibold">No sign-in on this deployment.</strong>
    Anyone who can reach it can act as
    <button type="button" class="underline hover:no-underline" @click="switchIdentity">
      {{ currentName }}
    </button>
    or anyone else.
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";

import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { usePersonStore } from "@/stores/person";

const personStore = usePersonStore();
const unauthenticated = bootstrapConfig().auth.unauthenticatedDeployment;

const currentName = computed(
  () => personStore.person?.displayName ?? personStore.person?.username ?? "anyone",
);

function switchIdentity() {
  personStore.switchIdentity().catch((error: unknown) => {
    console.error("ERROR => Failed to clear the claimed identity:", error);
  });
}
</script>
