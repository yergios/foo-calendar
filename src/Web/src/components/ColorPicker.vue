<template>
  <div class="grid grid-cols-8 gap-2 p-2 border border-border rounded-8 justify-items-center">
    <template v-if="loading">
      <AppSkeleton v-for="n in 8" :key="n" class="w-7 h-7 rounded-pill" />
    </template>
    <template v-else>
      <button
        v-for="c in visiblePalette"
        :key="c"
        type="button"
        class="relative w-7 h-7 rounded-pill border-2 transition-all duration-150 ease-out hover:scale-115 hover:shadow-card active:scale-95"
        :class="modelValue === c ? 'border-primary-dark scale-110' : 'border-transparent'"
        :style="{ backgroundColor: c }"
        :title="c"
        @click="$emit('update:modelValue', c)"
      >
        <svg
          v-if="modelValue === c"
          class="absolute inset-0 m-auto w-3.5 h-3.5 drop-shadow"
          :style="{ color: textColorFor(c) }"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="3"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <polyline points="20 6 9 17 4 12" />
        </svg>
      </button>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";

import AppSkeleton from "@/components/AppSkeleton.vue";
import { useContrastText } from "@/composables/useContrastText";

const { textColorFor } = useContrastText();

const props = withDefaults(
  defineProps<{
    modelValue: string | null;
    palette: string[];
    colorsInUse?: string[];
    exclude?: string | null;
    loading?: boolean;
  }>(),
  {
    colorsInUse: () => [],
    exclude: null,
    loading: false,
  },
);

defineEmits<{ "update:modelValue": [color: string] }>();

// Taken colors are left out of the grid entirely instead of shown-but-disabled. `exclude`
// (the person's own current color when editing) and the currently selected swatch always
// stay visible, even if they'd otherwise match something in colorsInUse.
const visiblePalette = computed(() =>
  props.palette.filter(
    (c) => !props.colorsInUse.includes(c) || c === props.exclude || c === props.modelValue,
  ),
);
</script>
