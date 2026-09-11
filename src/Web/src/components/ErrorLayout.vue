<template>
  <div class="min-h-screen flex flex-col items-center justify-center gap-4">
    <span class="font-display font-bold text-6xl text-primary-text opacity-20">{{ code }}</span>
    <p class="text-secondary text-sm">{{ message }}</p>
    <RouterLink
      v-if="action?.internal"
      :to="action.href"
      class="text-sm text-primary-text font-semibold hover:underline"
    >
      {{ action.label }}
    </RouterLink>
    <a
      v-else-if="action"
      :href="action.href"
      target="_blank"
      class="text-sm text-primary-text font-semibold hover:underline"
    >
      {{ action.label }}
    </a>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  code: string;
  message: string;
  action?: {
    label: string;
    href: string;
    internal?: boolean;
  };
}>();
</script>
