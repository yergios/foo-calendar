<template>
  <div class="flex flex-col h-screen overflow-hidden bg-background">
    <UnauthenticatedBanner />
    <AppHeader />
    <main class="flex-1 min-h-0">
      <div class="max-w-[1920px] mx-auto py-8 h-full flex flex-col gap-6">
        <RouterView />
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<script setup lang="ts">
import AppFooter from "@/components/AppFooter.vue";
import AppHeader from "@/components/AppHeader.vue";
import UnauthenticatedBanner from "@/components/UnauthenticatedBanner.vue";
</script>
