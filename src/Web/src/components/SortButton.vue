<template>
  <button
    type="button"
    class="inline-flex items-center gap-1 hover:text-ink transition-colors"
    :class="active ? 'text-ink' : 'text-secondary'"
    @click="$emit('toggle')"
  >
    <slot />
    <span v-if="active" class="text-[10px]" aria-hidden="true">{{ descending ? "▼" : "▲" }}</span>
  </button>
</template>

<script setup lang="ts">
defineProps<{ active: boolean; descending: boolean }>();
defineEmits<{ toggle: [] }>();
</script>
