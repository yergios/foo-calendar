<template>
  <div class="min-h-screen flex flex-col bg-background">
    <div class="max-w-3xl mx-auto w-full flex-1 px-6 py-10">
      <RouterLink to="/" class="text-sm text-primary-text hover:underline">
        ← Back to the calendar
      </RouterLink>

      <nav class="flex gap-6 border-b border-border mt-6 flex-wrap">
        <button
          v-for="page in legalPages"
          :key="page.key"
          type="button"
          class="py-3 text-sm font-semibold border-b-2 transition-colors"
          :class="
            activeKey === page.key
              ? 'border-accent text-accent'
              : 'border-transparent text-secondary hover:text-ink hover:border-border'
          "
          @click="selectTab(page.key)"
        >
          {{ page.title }}
        </button>
      </nav>

      <MarkdownContent v-if="activePage" :content="activePage.content" class="mt-2" />
    </div>

    <AppFooter />
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";

import AppFooter from "@/components/AppFooter.vue";
import MarkdownContent from "@/components/MarkdownContent.vue";
import { legalPages } from "@/lib/legal";

const route = useRoute();
const router = useRouter();

// The URL is the single source of truth for which tab is open, so a link straight to
// /legal?tab=privacy (see AppFooter) opens on the right tab and stays shareable.
const activeKey = computed(
  () => legalPages.find((p) => p.key === route.query.tab)?.key ?? legalPages[0]?.key,
);
const activePage = computed(() => legalPages.find((p) => p.key === activeKey.value));

function selectTab(key: string) {
  router.replace({ query: { ...route.query, tab: key } });
}
</script>
