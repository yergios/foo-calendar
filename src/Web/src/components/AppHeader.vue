<template>
  <header class="bg-background border-b border-border px-6 py-3">
    <div class="max-w-[1920px] mx-auto flex items-center justify-between gap-6">
      <RouterLink
        to="/"
        class="font-display font-bold text-2xl text-ink hover:text-accent transition shrink-0"
      >
        {{ appName }}
      </RouterLink>

      <div v-if="personStore.person" class="flex items-center gap-3">
        <nav class="flex items-center gap-1">
          <RouterLink
            v-for="item in navItems"
            :key="item.to"
            :to="item.to"
            class="font-display text-sm w-24 text-center py-1.5 rounded-pill transition-colors"
            :class="
              isActive(item.to)
                ? 'bg-surface text-ink font-semibold'
                : 'text-secondary hover:text-ink hover:bg-surface/60'
            "
          >
            {{ item.label }}
          </RouterLink>
        </nav>
        <NotificationBell />
      </div>

      <button
        v-else
        type="button"
        class="border border-accent text-accent rounded-pill px-3 py-1.5 text-sm font-semibold hover:bg-accent/10 transition-colors shrink-0"
        @click="personStore.ensurePerson()"
      >
        Set up profile
      </button>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed, onMounted } from "vue";
import { useRoute } from "vue-router";

import { useAuthStore } from "@/auth";
import NotificationBell from "@/components/NotificationBell.vue";
import { runtimeConfig } from "@/lib/runtimeConfig";
import { usePersonStore } from "@/stores/person";

const personStore = usePersonStore();
const authStore = useAuthStore();
const route = useRoute();
const appName = runtimeConfig().appName;

const navItems = computed(() => {
  const items = [
    { to: "/", label: "Home" },
    { to: "/profile", label: "Profile" },
  ];
  if (authStore.canAnything) items.push({ to: "/admin", label: "Admin" });
  return items;
});

// Prefix match, not just equality: /admin now redirects straight into one of its
// sections (see router/index.ts), so the nav item has to stay lit for /admin/people and
// the rest, not just for the bare /admin path nobody actually lands on.
function isActive(to: string): boolean {
  return route.path === to || route.path.startsWith(`${to}/`);
}

onMounted(() => {
  personStore.checkOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to check for an existing profile:", error);
  });
});
</script>
