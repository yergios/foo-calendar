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
        class="fixed z-[60] overflow-y-auto bg-background border border-border-strong rounded-8 shadow-card-hover py-1"
      >
        <li
          v-for="(choice, i) in matches"
          :id="`${id}-option-${i}`"
          :key="choice.value ?? ''"
          role="option"
          :aria-selected="choice.value === modelValue"
          class="px-3 py-1.5 text-sm text-ink cursor-pointer"
          :class="[
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

import type { RegionOption } from "@/types/config";

/**
 * One ISO 3166 code out of a list too long to scroll: 204 countries, or every subdivision
 * of one. A native <select> can neither be typed into nor kept to a sane height, which is
 * the whole reason this exists.
 */
interface Choice {
  /** null is the "nothing picked" choice, which is always offered first. */
  value: string | null;
  label: string;
}

const props = withDefaults(
  defineProps<{
    id: string;
    modelValue: string | null;
    options: RegionOption[];
    /** Label for the choice that clears the field, e.g. "Not set". */
    emptyLabel: string;
    placeholder?: string;
    disabled?: boolean;
  }>(),
  { placeholder: "Search…", disabled: false },
);

const emit = defineEmits<{ "update:modelValue": [value: string | null] }>();

const root = ref<HTMLElement | null>(null);
const field = ref<HTMLInputElement | null>(null);
const list = ref<HTMLElement | null>(null);

const open = ref(false);
const query = ref("");
const active = ref(0);
const panel = ref<Record<string, string>>({});

const choices = computed<Choice[]>(() => [
  { value: null, label: props.emptyLabel },
  ...props.options.map((o) => ({ value: o.code, label: o.label })),
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
  reposition();
  // Capture phase: the modal body scrolls, not the window, and that scroll doesn't bubble.
  window.addEventListener("scroll", reposition, true);
  window.addEventListener("resize", reposition);
  document.addEventListener("mousedown", onPointerDown);
  scrollActiveIntoView();
}

function hide() {
  if (!open.value) return;
  open.value = false;
  query.value = "";
  window.removeEventListener("scroll", reposition, true);
  window.removeEventListener("resize", reposition);
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

// Sized and placed against the viewport every time it could have moved. Drops upward only
// when there is genuinely more room up there, so it doesn't flip on a nearly full screen.
function reposition() {
  const el = root.value;
  if (!el) return;

  const rect = el.getBoundingClientRect();
  const below = window.innerHeight - rect.bottom - 8;
  const above = rect.top - 8;
  const dropUp = below < 176 && above > below;

  panel.value = {
    left: `${rect.left}px`,
    width: `${rect.width}px`,
    maxHeight: `${Math.min(288, dropUp ? above : below)}px`,
    ...(dropUp
      ? { bottom: `${window.innerHeight - rect.top + 4}px` }
      : { top: `${rect.bottom + 4}px` }),
  };
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
