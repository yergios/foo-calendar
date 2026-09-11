<template>
  <Teleport to="body">
    <!-- Always in the DOM, never v-if'd: the box has to survive its own fade-out, and
         keeping one element means showing a tooltip costs a couple of style writes rather
         than a mount. pointer-events-none so it can never sit between the cursor and the
         thing it is describing. -->
    <div
      id="app-tooltip"
      ref="tooltipRef"
      class="app-tooltip fixed z-[70] pointer-events-none max-w-80 rounded-4 bg-ink text-background shadow-overlay px-2 py-1 text-xs whitespace-pre-line"
      role="tooltip"
      :data-shown="shown"
      :data-placement="placement"
    >
      {{ displayText }}
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";

import { hideTooltip, showTooltip, tooltip } from "@/lib/tooltip";

/** Distance from the anchor, and the closest the box may come to the viewport edge. */
const GAP = 6;
const MARGIN = 8;

const tooltipRef = ref<HTMLElement | null>(null);
// The text lags the store by one step on the way out, so the box fades at the size and place
// it was shown at instead of collapsing to empty first.
const displayText = ref("");
const shown = ref(false);
const placement = ref<"above" | "below">("above");
// The anchor we pointed aria-describedby at, so it can be cleaned off the right element even
// after the store has moved on.
let described: HTMLElement | null = null;

watch(
  () => tooltip.seq,
  async () => {
    if (!tooltip.text || !tooltip.anchor) return;
    const anchor = tooltip.anchor;
    displayText.value = tooltip.text;
    // Let the new text land before measuring, or the box is sized for the previous one.
    await nextTick();
    position(anchor);
    describe(anchor);
    shown.value = true;
  },
);

watch(
  () => tooltip.text,
  (text) => {
    if (text) return;
    shown.value = false;
    describe(null);
  },
);

function position(anchor: HTMLElement) {
  const el = tooltipRef.value;
  if (!el) return;

  const rect = anchor.getBoundingClientRect();
  const { offsetWidth: width, offsetHeight: height } = el;

  // Below by default, so the box never covers the thing it is describing, and above only
  // when there isn't room for it underneath.
  const below = rect.bottom + GAP + height <= window.innerHeight - MARGIN;
  placement.value = below ? "below" : "above";

  const left = rect.left + rect.width / 2 - width / 2;
  el.style.left = `${clamp(left, MARGIN, window.innerWidth - width - MARGIN)}px`;
  el.style.top = `${below ? rect.bottom + GAP : rect.top - height - GAP}px`;
}

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), Math.max(min, max));
}

function describe(anchor: HTMLElement | null) {
  if (described === anchor) return;
  described?.removeAttribute("aria-describedby");
  described = anchor;
  described?.setAttribute("aria-describedby", "app-tooltip");
}

// One set of listeners for the whole app rather than a pair per tooltip. pointerover /
// pointerout (not enter/leave) because only these bubble, which is what makes the
// delegation possible; focusin/focusout give keyboard users what the mouse gets, which a
// native `title` never did.
function anchorFrom(target: EventTarget | null): HTMLElement | null {
  return target instanceof Element ? target.closest<HTMLElement>("[data-tooltip]") : null;
}

function handleOver(event: PointerEvent | FocusEvent) {
  const anchor = anchorFrom(event.target);
  if (anchor) showTooltip(anchor, anchor.dataset.tooltip ?? "");
}

function handleOut(event: PointerEvent | FocusEvent) {
  const anchor = anchorFrom(event.target);
  if (!anchor) return;
  // Crossing between children of the same anchor is not leaving it.
  const next = event.relatedTarget;
  if (next instanceof Node && anchor.contains(next)) return;
  hideTooltip();
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key === "Escape") hideTooltip();
}

onMounted(() => {
  document.addEventListener("pointerover", handleOver);
  document.addEventListener("pointerout", handleOut);
  document.addEventListener("focusin", handleOver);
  document.addEventListener("focusout", handleOut);
  document.addEventListener("keydown", handleKeydown);
  // Capture phase: the thing that scrolled is often not the window - the calendar grid
  // scrolls its own box. Hiding beats following; the next pointermove brings it back.
  window.addEventListener("scroll", hideTooltip, true);
});

onBeforeUnmount(() => {
  document.removeEventListener("pointerover", handleOver);
  document.removeEventListener("pointerout", handleOut);
  document.removeEventListener("focusin", handleOver);
  document.removeEventListener("focusout", handleOut);
  document.removeEventListener("keydown", handleKeydown);
  window.removeEventListener("scroll", hideTooltip, true);
  describe(null);
});
</script>

<style scoped>
/* Hidden by opacity rather than `hidden`, so there is something to fade. The rise is 2px
   against the direction it opens from - enough to read as movement without being a journey. */
.app-tooltip {
  opacity: 0;
  transform: translateY(2px);
  transition:
    opacity var(--default-transition-duration) var(--default-transition-timing-function),
    transform var(--default-transition-duration) var(--default-transition-timing-function);
}

.app-tooltip[data-placement="below"] {
  transform: translateY(-2px);
}

.app-tooltip[data-shown="true"] {
  opacity: 1;
  transform: none;
}

@media (prefers-reduced-motion: reduce) {
  .app-tooltip,
  .app-tooltip[data-placement="below"] {
    transform: none;
  }
}
</style>
