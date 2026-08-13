<template>
  <div class="flex flex-col gap-2">
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
          :class="modelValue === c ? 'border-ink scale-110' : 'border-transparent'"
          :style="{ backgroundColor: c }"
          :title="c"
          @click="selectFromPalette(c)"
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

    <div class="flex items-center gap-2">
      <input
        type="color"
        class="w-9 h-9 shrink-0 rounded-8 border border-border-strong bg-transparent p-0 cursor-pointer"
        :value="swatchValue"
        title="Pick a custom color"
        @input="onPick"
      />
      <input
        type="text"
        maxlength="7"
        spellcheck="false"
        placeholder="Or a custom color, e.g. #4a90d2"
        class="flex-1 min-w-0 border rounded-8 px-3 py-2 font-mono text-sm"
        :class="customError ? 'border-error' : 'border-border-strong'"
        :value="customHex"
        @input="onType"
      />
    </div>

    <p v-if="customError" class="text-sm text-error">{{ customError }}</p>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";

import AppSkeleton from "@/components/AppSkeleton.vue";
import { textColorFor } from "@/lib/contrastText";

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

// null while the typed color is unusable, so the form can keep its Save button disabled
// instead of silently submitting whatever was selected before.
const emit = defineEmits<{ "update:modelValue": [color: string | null] }>();

const HEX = /^#[0-9a-f]{6}$/;
const FALLBACK_SWATCH = "#4a90d2";

// Hex is compared case-insensitively everywhere: the palette and the API are lower-case,
// but a person typing their brand color will just as happily write "#FF4500".
function normalize(color: string): string {
  const trimmed = color.trim().toLowerCase();
  return trimmed.startsWith("#") || trimmed.length === 0 ? trimmed : `#${trimmed}`;
}

const inPalette = (color: string) => props.palette.some((c) => normalize(c) === color);

// Taken means "belongs to someone else". `exclude` is the person's own current color when
// editing, which stays theirs to keep.
const isTaken = (color: string) =>
  props.colorsInUse.some((c) => normalize(c) === color) && color !== normalize(props.exclude ?? "");

// Taken colors are left out of the grid entirely instead of shown-but-disabled. `exclude`
// and the currently selected swatch always stay visible, even if they'd otherwise match
// something in colorsInUse.
const visiblePalette = computed(() =>
  props.palette.filter((c) => !isTaken(normalize(c)) || c === props.modelValue),
);

// Only holds a color that isn't one of the swatches; picking from the grid empties it.
const customHex = ref(
  props.modelValue && !inPalette(normalize(props.modelValue)) ? normalize(props.modelValue) : "",
);

const customError = computed(() => {
  const hex = normalize(customHex.value);
  if (hex.length === 0) return null;
  if (!HEX.test(hex)) return "Use a 6-digit hex color, for example #4a90d2.";
  if (isTaken(hex)) return "That color is already used by someone else. Pick another one.";
  return null;
});

// The native picker has no "empty" state, so it falls back to a readable default rather
// than showing black for a half-typed value.
const swatchValue = computed(() => {
  const hex = normalize(customHex.value);
  return HEX.test(hex) ? hex : FALLBACK_SWATCH;
});

// An empty field selects nothing at all, rather than leaving the previously selected color
// submitted behind the user's back. Same for a half-typed or taken one.
function setCustom(value: string) {
  customHex.value = value;
  const hex = normalize(value);
  emit("update:modelValue", hex.length > 0 && customError.value === null ? hex : null);
}

function onType(event: Event) {
  setCustom((event.target as HTMLInputElement).value);
}

function onPick(event: Event) {
  setCustom(normalize((event.target as HTMLInputElement).value));
}

function selectFromPalette(color: string) {
  customHex.value = "";
  emit("update:modelValue", color);
}
</script>
