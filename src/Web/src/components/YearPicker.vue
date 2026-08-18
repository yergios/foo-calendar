<template>
  <div class="flex items-center gap-1.5">
    <button
      type="button"
      class="w-9 h-9 flex items-center justify-center rounded-8 hover:bg-surface text-primary-text transition-colors"
      aria-label="Previous year"
      @click="$emit('update:modelValue', modelValue - 1)"
    >
      &larr;
    </button>
    <slot>{{ modelValue }}</slot>
    <button
      type="button"
      class="w-9 h-9 flex items-center justify-center rounded-8 hover:bg-surface text-primary-text transition-colors"
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
