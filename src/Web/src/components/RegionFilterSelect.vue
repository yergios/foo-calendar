<template>
  <div ref="root" class="relative">
    <button
      :id="id"
      type="button"
      class="h-9 w-48 flex items-center justify-between gap-2 border border-border-strong rounded-8 pl-3 pr-2 text-sm bg-background hover:bg-surface/60 transition-colors"
      :aria-expanded="open"
      @click="togglePanel"
    >
      <span class="truncate text-left">{{ summary }}</span>
      <svg
        class="w-4 h-4 text-secondary shrink-0"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <polyline points="6 9 12 15 18 9" />
      </svg>
    </button>

    <!-- Teleported for the same reason as RegionSelect: an absolutely positioned panel
         would be clipped by any ancestor that scrolls its own content. -->
    <Teleport to="body">
      <div
        v-if="open"
        ref="panel"
        role="listbox"
        aria-multiselectable="true"
        :style="panelStyle"
        class="fixed z-[60] overflow-y-auto bg-background border border-border-strong rounded-8 shadow-card-hover py-1"
      >
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
              @change="toggleValue(region.code)"
            />
            {{ region.label }}
          </label>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { computed, onUnmounted, ref } from "vue";

import { useFloatingPanel } from "@/composables/useFloatingPanel";
import type { RegionGroup } from "@/lib/regionGroups";

/**
 * Lets several regions/countries be picked at once, grouped the way the team actually
 * occupies them. A native <select multiple> can't show that grouping without going tall
 * and ungainly, which is why this exists as its own component rather than adding a
 * `multiple` prop to RegionSelect (whose single-pick, type-to-search UI is built around
 * ProfileForm's very different job: choosing exactly one region out of every one in the
 * world, not filtering within the ones the team already has).
 */
const props = withDefaults(
  defineProps<{
    id: string;
    modelValue: string[];
    groups: RegionGroup[];
    emptyLabel?: string;
  }>(),
  { emptyLabel: "All regions" },
);

const emit = defineEmits<{ "update:modelValue": [value: string[]] }>();

const root = ref<HTMLElement | null>(null);
const panel = ref<HTMLElement | null>(null);
const open = ref(false);
const { style: panelStyle, track: trackPanel, untrack: untrackPanel } = useFloatingPanel(root, {
  flipThreshold: 240,
  maxHeight: 320,
  minWidth: 240,
});

function isSelected(code: string): boolean {
  return props.modelValue.includes(code);
}

function toggleValue(code: string) {
  const next = isSelected(code)
    ? props.modelValue.filter((c) => c !== code)
    : [...props.modelValue, code];
  emit("update:modelValue", next);
}

function labelFor(code: string): string {
  for (const group of props.groups) {
    if (group.code === code) return `${group.label} (all)`;
    const region = group.regions.find((r) => r.code === code);
    if (region) return region.label;
  }
  return code;
}

const summary = computed(() => {
  if (props.modelValue.length === 0) return props.emptyLabel;
  if (props.modelValue.length === 1) return labelFor(props.modelValue[0]!);
  return `${props.modelValue.length} regions`;
});

function togglePanel() {
  if (open.value) {
    close();
    return;
  }
  open.value = true;
  trackPanel();
  document.addEventListener("mousedown", onPointerDown);
  document.addEventListener("keydown", onKeydown);
}

function close() {
  if (!open.value) return;
  open.value = false;
  untrackPanel();
  document.removeEventListener("mousedown", onPointerDown);
  document.removeEventListener("keydown", onKeydown);
}

onUnmounted(close);

function onPointerDown(event: MouseEvent) {
  const target = event.target as Node;
  if (root.value?.contains(target) || panel.value?.contains(target)) return;
  close();
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === "Escape") close();
}
</script>
