<template>
  <button
    type="button"
    class="inline-flex items-center gap-1 hover:text-ink transition-colors"
    :class="active ? 'text-ink' : 'text-secondary'"
    :title="title"
    @click="$emit('toggle')"
  >
    <slot />
    <!-- Always shown, not just when active: its color already follows the button's own
         (text-secondary when inactive, text-ink when active), so this alone is what makes
         an unapplied column read as a muted arrow rather than no indicator at all.
         `descending` only means anything for the currently active column - callers pass
         the same ref to every column's button, so an inactive one ignores it and always
         shows the neutral ascending arrow rather than borrowing whichever direction the
         active column happens to be sorted in. -->
    <span class="text-[10px]" aria-hidden="true">{{ active && descending ? "▼" : "▲" }}</span>
  </button>
</template>

<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{ active: boolean; descending: boolean; label: string }>();
defineEmits<{ toggle: [] }>();

const title = computed(() => {
  if (!props.active) return `Sort by ${props.label}`;
  return props.descending ? `Sorted by ${props.label}, descending` : `Sorted by ${props.label}, ascending`;
});
</script>
