<template>
  <div ref="root" class="relative">
    <button
      ref="trigger"
      type="button"
      class="relative w-9 h-9 flex items-center justify-center rounded-pill border transition-colors"
      :class="
        open
          ? 'border-border-strong bg-surface text-ink'
          : 'border-transparent text-secondary hover:text-ink hover:bg-surface/60'
      "
      :title="title"
      :aria-expanded="open"
      @click="togglePanel"
    >
      <slot name="icon" />
      <!-- The control behind this button is out of sight while the panel is closed, so a
           setting that's away from its default has to say so here or it's invisible. -->
      <span v-if="active" class="absolute top-1 right-1 w-1.5 h-1.5 rounded-pill bg-accent" />
    </button>

    <!-- Teleported for the same reason as RegionFilterSelect's own panel: an absolutely
         positioned one would be clipped by any ancestor that scrolls its own content. -->
    <Teleport to="body">
      <div
        v-if="open"
        ref="panel"
        :style="panelStyle"
        class="fixed z-[60] overflow-y-auto bg-background border border-border-strong rounded-8 shadow-card-hover py-1"
      >
        <slot />
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onUnmounted, ref } from "vue";

import { useFloatingPanel } from "@/composables/useFloatingPanel";

/**
 * An icon button that drops a floating panel of whatever you put in its default slot.
 * The open/close plumbing is the same as RegionFilterSelect's, which is why it lives in
 * one place now that the header needs two of these.
 */
const props = withDefaults(defineProps<{ title: string; active?: boolean; minWidth?: number }>(), {
  active: false,
  minWidth: 240,
});

const root = ref<HTMLElement | null>(null);
const trigger = ref<HTMLButtonElement | null>(null);
const panel = ref<HTMLElement | null>(null);
const open = ref(false);

// The button is 36px wide, so without a minimum the panel would come out the same width.
const {
  style: panelStyle,
  track: trackPanel,
  untrack: untrackPanel,
} = useFloatingPanel(root, {
  flipThreshold: 240,
  maxHeight: 360,
  minWidth: props.minWidth,
});

const FOCUSABLE =
  'button, [href], input:not([type="hidden"]), select, textarea, [tabindex]:not([tabindex="-1"])';

async function togglePanel() {
  if (open.value) {
    close();
    return;
  }
  open.value = true;
  trackPanel();
  document.addEventListener("mousedown", onPointerDown);
  document.addEventListener("keydown", onKeydown);

  // The panel is teleported to the end of <body>, so tabbing out of the trigger goes to
  // the next thing in the header, not into what was just opened. Moving focus in is what
  // the native <select> this replaced did for free.
  await nextTick();
  panel.value?.querySelector<HTMLElement>(FOCUSABLE)?.focus();
}

function close() {
  if (!open.value) return;
  // Only take focus back when it's still inside the panel. Closing because the user
  // clicked somewhere else on the page shouldn't drag focus onto this button.
  const focusWasInside = panel.value?.contains(document.activeElement) === true;

  open.value = false;
  untrackPanel();
  document.removeEventListener("mousedown", onPointerDown);
  document.removeEventListener("keydown", onKeydown);

  if (focusWasInside) trigger.value?.focus();
}

defineExpose({ close });

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
