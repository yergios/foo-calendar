<template>
  <div ref="root" class="relative">
    <input
      :id="id"
      ref="field"
      type="text"
      role="combobox"
      autocomplete="off"
      aria-autocomplete="list"
      :aria-expanded="open"
      :aria-controls="`${id}-list`"
      :aria-activedescendant="open && active >= 0 ? `${id}-option-${active}` : undefined"
      :value="open ? query : selectedLabel"
      :placeholder="open ? selectedLabel || placeholder : placeholder"
      :disabled="disabled"
      class="w-full border border-border-strong rounded-8 pl-3 pr-9 py-2 bg-background disabled:opacity-40 disabled:cursor-not-allowed"
      @focus="show"
      @click="show"
      @input="onInput"
      @keydown="onKeydown"
    />
    <svg
      class="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-secondary pointer-events-none"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      <polyline points="6 9 12 15 18 9" />
    </svg>

    <!-- Teleported because the form lives inside a modal that scrolls its own body: an
         absolutely positioned list would be clipped by that overflow. -->
    <Teleport to="body">
      <ul
        v-if="open"
        :id="`${id}-list`"
        ref="list"
        role="listbox"
        :style="panel"
        class="fixed z-[60] overflow-y-auto bg-background border border-border-strong rounded-8 shadow-overlay py-1"
      >
        <li
          v-for="(choice, i) in matches"
          :id="`${id}-option-${i}`"
          :key="choice.value ?? ''"
          role="option"
          :aria-selected="choice.value === modelValue"
          class="py-1.5 text-sm text-ink cursor-pointer"
          :class="[
            choice.group ? 'pl-7 pr-3' : 'px-3',
            isGrouped && choice.value !== null && !choice.group ? 'font-semibold' : '',
            i === active ? 'bg-surface' : '',
            choice.value === modelValue ? 'font-bold' : '',
          ]"
          @mousedown.prevent="pick(choice)"
          @mouseenter="active = i"
        >
          {{ choice.label }}
        </li>
        <li v-if="matches.length === 0" class="px-3 py-1.5 text-sm text-secondary">No matches</li>
      </ul>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from "vue";

import { useFloatingPanel } from "@/composables/useFloatingPanel";
import type { RegionOption } from "@/types/config";

/** A plain RegionOption, plus which group (if any) it renders indented under - see the
 * `options` prop doc below. Every existing caller's plain RegionOption[] is still valid
 * here, since `group` is optional. */
export interface RegionSelectOption extends RegionOption {
  group?: string;
}

/**
 * One ISO 3166 code out of a list too long to scroll: 204 countries, or every subdivision
 * of one. A native <select> can neither be typed into nor kept to a sane height, which is
 * the whole reason this exists.
 */
interface Choice {
  /** null is the "nothing picked" choice, which is always offered first. */
  value: string | null;
  label: string;
  group?: string;
}

const props = withDefaults(
  defineProps<{
    id: string;
    modelValue: string | null;
    /** `group` is optional per option and purely cosmetic: set it (to the country's own
     * label) on a subdivision to have it render indented under that country's entry, the
     * same shape RegionFilterSelect groups by, just single-pick and searchable instead of
     * checkboxes. Leave every option's `group` unset for a plain flat list, today's
     * behavior and what every caller besides the bank-holiday sync picker still wants. */
    options: RegionSelectOption[];
    /** Label for the choice that clears the field, e.g. "Not set". */
    emptyLabel: string;
    placeholder?: string;
    disabled?: boolean;
  }>(),
  { placeholder: "Search…", disabled: false },
);

const emit = defineEmits<{ "update:modelValue": [value: string | null] }>();

// Whether ANY option opted into grouping, not per-choice: a country's own entry has no
// `group` either (it isn't indented under itself), so "has a group" alone can't tell a
// country header apart from a plain, ungrouped list's every option.
const isGrouped = computed(() => props.options.some((o) => o.group));

const root = ref<HTMLElement | null>(null);
const field = ref<HTMLInputElement | null>(null);
const list = ref<HTMLElement | null>(null);

const open = ref(false);
const query = ref("");
const active = ref(0);
const {
  style: panel,
  reposition,
  track: trackPanel,
  untrack: untrackPanel,
} = useFloatingPanel(root, { flipThreshold: 176, maxHeight: 288 });

const choices = computed<Choice[]>(() => [
  { value: null, label: props.emptyLabel },
  ...props.options.map((o) => ({ value: o.code, label: o.label, group: o.group })),
]);

// Falls back to the raw code so a saved region still reads as something while its
// country's list is loading, or if the source has since stopped offering it.
const selectedLabel = computed(
  () => props.options.find((o) => o.code === props.modelValue)?.label ?? props.modelValue ?? "",
);

// Accents are dropped on both sides so "zurich" finds Zürich and "leon" finds León.
function fold(text: string): string {
  return text
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase();
}

const matches = computed(() => {
  const needle = fold(query.value.trim());
  if (!needle) return choices.value;
  // The code is searchable too: someone who knows "ES-MD" shouldn't have to recall "Madrid".
  return choices.value.filter(
    (c) => fold(c.label).includes(needle) || fold(c.value ?? "").includes(needle),
  );
});

function show() {
  if (open.value || props.disabled) return;
  query.value = "";
  active.value = Math.max(
    0,
    matches.value.findIndex((c) => c.value === props.modelValue),
  );
  open.value = true;
  trackPanel();
  document.addEventListener("mousedown", onPointerDown);
  scrollActiveIntoView();
}

function hide() {
  if (!open.value) return;
  open.value = false;
  query.value = "";
  untrackPanel();
  document.removeEventListener("mousedown", onPointerDown);
}

// Closing on the input's own blur would also fire when the list's scrollbar is grabbed,
// so what counts is where the press landed, not where focus went.
function onPointerDown(event: MouseEvent) {
  const target = event.target as Node;
  if (root.value?.contains(target) || list.value?.contains(target)) return;
  hide();
}

onUnmounted(hide);

function pick(choice: Choice) {
  emit("update:modelValue", choice.value);
  hide();
  field.value?.blur();
}

function onInput(event: Event) {
  query.value = (event.target as HTMLInputElement).value;
  active.value = 0;
  if (!open.value) show();
  reposition();
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === "Escape") {
    hide();
    return;
  }
  if (event.key === "Tab") {
    hide();
    return;
  }
  if (event.key === "Enter") {
    // Swallowed even when closed, so picking a region never submits the form around it.
    event.preventDefault();
    const choice = matches.value[active.value];
    if (open.value && choice) pick(choice);
    return;
  }
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;

  event.preventDefault();
  if (!open.value) {
    show();
    return;
  }
  const step = event.key === "ArrowDown" ? 1 : -1;
  const count = matches.value.length;
  if (count > 0) active.value = (active.value + step + count) % count;
  scrollActiveIntoView();
}

function scrollActiveIntoView() {
  nextTick(() => list.value?.children[active.value]?.scrollIntoView({ block: "nearest" }));
}

// The list shrinks as you type, so an index that was valid a keystroke ago may not be.
watch(matches, (current) => {
  if (active.value >= current.length) active.value = Math.max(0, current.length - 1);
});

// Clearing the country disables the region field while its list is still hanging open.
watch(
  () => props.disabled,
  (isDisabled) => {
    if (isDisabled) hide();
  },
);
</script>
