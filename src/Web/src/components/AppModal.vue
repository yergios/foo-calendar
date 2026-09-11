<template>
  <Teleport to="body">
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      @click.self="$emit('close')"
    >
      <div
        class="bg-background rounded-12 shadow-overlay flex flex-col w-full max-h-[80vh]"
        :class="size === 'md' ? 'max-w-md' : 'max-w-3xl'"
        :role="role"
        aria-modal="true"
      >
        <div class="flex items-center justify-between px-6 py-4 border-b border-border shrink-0">
          <h5 class="font-display text-ink">{{ title }}</h5>
          <button
            class="w-9 h-9 flex items-center justify-center rounded-pill text-secondary hover:text-ink hover:bg-surface/60 transition-colors"
            aria-label="Close"
            @click="$emit('close')"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              class="w-5 h-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <path d="M18 6L6 18M6 6l12 12" />
            </svg>
          </button>
        </div>
        <div v-if="$slots.tabs" class="px-6 border-b border-border shrink-0">
          <slot name="tabs" />
        </div>
        <div class="overflow-y-auto flex-1 px-6 py-5">
          <slot />
        </div>
        <div v-if="$slots.footer" class="px-6 py-4 border-t border-border shrink-0">
          <slot name="footer" />
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted } from "vue";

withDefaults(
  defineProps<{ title: string; size?: "md" | "lg"; role?: "dialog" | "alertdialog" }>(),
  { size: "lg", role: "dialog" },
);
const emit = defineEmits<{ close: [] }>();

// Escape closes, for every modal in the app at once - until now the only ways out were the
// X and the backdrop. Same keydown pattern as IconPopover's.
function handleKeydown(event: KeyboardEvent) {
  if (event.key === "Escape") emit("close");
}

onMounted(() => document.addEventListener("keydown", handleKeydown));
onBeforeUnmount(() => document.removeEventListener("keydown", handleKeydown));
</script>
