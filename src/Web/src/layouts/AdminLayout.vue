<template>
  <div class="h-full min-h-0 flex gap-8">
    <nav class="w-44 shrink-0 flex flex-col gap-1">
      <RouterLink
        v-for="item in navItems"
        :key="item.path"
        :to="`/admin/${item.path}`"
        class="text-sm px-3 py-2 rounded-8 transition-colors"
        :class="
          isActive(item.path)
            ? 'bg-surface text-ink font-semibold'
            : 'text-secondary hover:text-ink hover:bg-surface/60'
        "
      >
        {{ item.label }}
      </RouterLink>
    </nav>

    <!-- Its own scroll region, not the page as a whole: the app shell is a fixed
         viewport height (see AppLayout), so a section taller than the screen needs
         somewhere to scroll rather than just getting clipped. -->
    <div class="flex-1 min-w-0 min-h-0 overflow-y-auto">
      <RouterView />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";

import { useAuthStore } from "@/auth";
import { ADMIN_SECTIONS } from "@/router/adminSections";

const authStore = useAuthStore();
const route = useRoute();

// Same permission check the router itself enforces per section (see adminSections.ts
// and the router guard), so the sidebar never offers a link that would just bounce back
// to /403.
const navItems = computed(() =>
  ADMIN_SECTIONS.filter((section) => section.permissions.some((p) => authStore.can(p))),
);

function isActive(path: string): boolean {
  return route.path === `/admin/${path}`;
}
</script>
