<template>
  <!-- role is the point of this component, not the colours: an inline <p> that appears after
       a failed save is invisible to a screen reader, which is what every one of these was
       before. "alert" interrupts for a failure; "status" waits its turn for a success. -->
  <p :class="variantClass" :role="variant === 'error' ? 'alert' : 'status'">
    <!-- No inline colour: fill="currentColor" inherits text-green from the parent, so the
         tick stays readable in dark mode too. -->
    <svg
      v-if="variant === 'success'"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      class="shrink-0"
      width="16"
      height="16"
      viewBox="0 0 24 24"
    >
      <path
        fill="currentColor"
        d="m10.6 13.8l-2.15-2.15q-.275-.275-.7-.275t-.7.275t-.275.7t.275.7L9.9 15.9q.3.3.7.3t.7-.3l5.65-5.65q.275-.275.275-.7t-.275-.7t-.7-.275t-.7.275zM12 22q-2.075 0-3.9-.788t-3.175-2.137T2.788 15.9T2 12t.788-3.9t2.137-3.175T8.1 2.788T12 2t3.9.788t3.175 2.137T21.213 8.1T22 12t-.788 3.9t-2.137 3.175t-3.175 2.138T12 22"
      ></path>
    </svg>
    <slot />
  </p>
</template>

<script setup lang="ts">
import { computed } from "vue";

const props = withDefaults(defineProps<{ variant?: "error" | "success" }>(), {
  variant: "error",
});

// An error is a filled block because it's the reason something didn't happen and has to be
// found; a success is a line of text with a tick, because it's confirming what you already
// expected. Spacing stays at the call site - these sit in a dozen different layouts.
const variantClass = computed(() =>
  props.variant === "error"
    ? "flex items-center gap-1.5 text-sm text-error bg-error-bg rounded-8 px-3 py-2"
    : "flex items-center gap-1.5 text-green",
);
</script>
