<template>
  <div class="flex items-center gap-3">
    <button
      type="button"
      class="px-3 py-1 rounded-8 hover:bg-surface text-primary transition-colors"
      aria-label="Previous year"
      @click="$emit('update:modelValue', modelValue - 1)"
    >
      &larr;
    </button>
    <slot>{{ modelValue }}</slot>
    <button
      type="button"
      class="px-3 py-1 rounded-8 hover:bg-surface text-primary transition-colors"
      aria-label="Next year"
      @click="$emit('update:modelValue', modelValue + 1)"
    >
      &rarr;
    </button>
  </div>
</template>

<script setup lang="ts">
defineProps<{ modelValue: number }>();
defineEmits<{ "update:modelValue": [year: number] }>();
</script>
