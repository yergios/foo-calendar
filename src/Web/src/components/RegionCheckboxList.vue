<template>
  <div role="listbox" aria-multiselectable="true">
    <!-- Not just the empty summary label's implicit meaning - an explicit row so
         clearing a multi-region selection is one click instead of unchecking each
         box by hand. Always resets to no filter, regardless of the checkbox's own
         prior state, rather than toggling back and forth. -->
    <label
      class="flex items-center gap-2 px-3 py-1.5 text-sm text-ink cursor-pointer hover:bg-surface border-b border-border"
    >
      <input
        type="checkbox"
        class="shrink-0"
        :checked="modelValue.length === 0"
        @change="selectAll"
      />
      All
    </label>

    <p v-if="groups.length === 0" class="px-3 py-1.5 text-sm text-secondary">No regions yet</p>

    <!-- Grouped by country, with each region indented underneath it, so the
         hierarchy reads the same way the old <optgroup> list did. Checking a
         country selects everyone in it (including future subdivisions), same
         matching rule as before; checking a region only narrows to that one. -->
    <div v-for="group in groups" :key="group.code">
      <label
        class="flex items-center gap-2 px-3 py-1.5 text-xs font-semibold text-secondary uppercase tracking-wide cursor-pointer hover:bg-surface"
      >
        <input
          type="checkbox"
          class="shrink-0"
          :checked="isSelected(group.code)"
          @change="toggleValue(group.code)"
        />
        {{ group.label }} (all)
      </label>
      <label
        v-for="region in group.regions"
        :key="region.code"
        class="flex items-center gap-2 pl-7 pr-3 py-1.5 text-sm text-ink cursor-pointer hover:bg-surface"
      >
        <input
          type="checkbox"
          class="shrink-0"
          :checked="isSelected(region.code)"
          data-perf="region"
          @change="toggleValue(region.code)"
        />
        {{ region.label }}
      </label>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { RegionGroup } from "@/lib/regionGroups";

/**
 * Just the grouped list of region checkboxes, with no dropdown of its own. Split out of
 * RegionFilterSelect so the home page's filter popover can show the regions directly
 * instead of putting a second dropdown inside a panel that's already floating.
 */
const props = defineProps<{
  modelValue: string[];
  groups: RegionGroup[];
}>();

const emit = defineEmits<{ "update:modelValue": [value: string[]] }>();

function isSelected(code: string): boolean {
  return props.modelValue.includes(code);
}

function toggleValue(code: string) {
  const next = isSelected(code)
    ? props.modelValue.filter((c) => c !== code)
    : [...props.modelValue, code];
  emit("update:modelValue", next);
}

function selectAll() {
  emit("update:modelValue", []);
}
</script>
