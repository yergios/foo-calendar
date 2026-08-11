<template>
  <div class="flex flex-col min-h-screen bg-white">
    <AppHeader />
    <main class="flex-1">
      <div class="max-w-[1920px] mx-auto px-4 py-8 flex flex-col gap-6">
        <RouterView />
      </div>
    </main>
    <AppFooter />
  </div>
</template>

<script setup lang="ts">
import AppFooter from "@/components/AppFooter.vue";
import AppHeader from "@/components/AppHeader.vue";
</script>
