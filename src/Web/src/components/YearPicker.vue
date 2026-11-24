<template>
  <div class="flex items-center gap-1.5">
    <button
      type="button"
      class="w-9 h-9 flex items-center justify-center rounded-pill text-secondary hover:text-ink hover:bg-surface/60 transition-colors"
      aria-label="Previous year"
      data-perf="year-previous"
      @click="$emit('update:modelValue', modelValue - 1)"
    >
      &larr;
    </button>
    <slot>{{ modelValue }}</slot>
    <button
      type="button"
      class="w-9 h-9 flex items-center justify-center rounded-pill text-secondary hover:text-ink hover:bg-surface/60 transition-colors"
      aria-label="Next year"
      data-perf="year-next"
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
