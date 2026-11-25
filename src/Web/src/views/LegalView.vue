<template>
  <div class="min-h-screen flex flex-col bg-background">
    <div class="max-w-3xl mx-auto w-full flex-1 px-6 py-10">
      <RouterLink to="/" class="text-sm text-primary-text hover:underline">
        ← Back to the calendar
      </RouterLink>

      <MarkdownContent :content="legalPages[page]" class="mt-2" />
    </div>

    <AppFooter />
  </div>
</template>

<script setup lang="ts">
import AppFooter from "@/components/AppFooter.vue";
import MarkdownContent from "@/components/MarkdownContent.vue";
import { type LegalPageKey, legalPages } from "@/lib/legal";

// Set by the route, so /privacy and /terms are each a page of their own to link to.
defineProps<{ page: LegalPageKey }>();
</script>
