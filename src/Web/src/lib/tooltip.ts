import { reactive } from "vue";

/**
 * The app's one tooltip. Every tooltip in the UI is this single element moved and refilled,
 * never a component per anchor: the calendar grid alone puts one on 365 memoized day headers
 * and on a hover overlay it re-points on every pointermove, and an instance each would undo
 * exactly the work those memos exist to do.
 *
 * Two ways to drive it. Almost every call site just puts `data-tooltip="..."` on an element
 * and AppTooltip's delegated listeners do the rest. CalendarGrid drives it through the
 * functions below instead, because its cell tooltip lives on a single overlay element that
 * moves under a still cursor: nothing ever re-enters, so no pointerover would fire for the
 * new text.
 */

/** Long enough that sweeping across a row of cells doesn't strobe, short enough not to feel
 * gated. The browser's own ~500ms+ is what makes a native `title` feel sluggish. */
const OPEN_DELAY_MS = 120;
/** Once one tooltip has been shown, the next opens with no delay at all - what makes moving
 * along a row of icon buttons feel immediate instead of re-gated at every step. */
const WARM_MS = 300;

export const tooltip = reactive({
  text: "",
  anchor: null as HTMLElement | null,
  /** Bumped on every show. AppTooltip watches this rather than the text, so that moving to a
   * different cell with the same words still repositions the box. */
  seq: 0,
});

let openTimer: ReturnType<typeof setTimeout> | null = null;
let warmUntil = 0;

export function showTooltip(anchor: HTMLElement, text: string) {
  if (!text) {
    hideTooltip();
    return;
  }
  clearOpenTimer();
  // Already showing, or only just stopped: swap straight over without re-serving the delay.
  if (tooltip.text || performance.now() < warmUntil) {
    assign(anchor, text);
    return;
  }
  openTimer = setTimeout(() => {
    openTimer = null;
    assign(anchor, text);
  }, OPEN_DELAY_MS);
}

export function hideTooltip() {
  clearOpenTimer();
  if (!tooltip.text) return;
  warmUntil = performance.now() + WARM_MS;
  tooltip.text = "";
  tooltip.anchor = null;
}

function assign(anchor: HTMLElement, text: string) {
  tooltip.anchor = anchor;
  tooltip.text = text;
  tooltip.seq++;
}

function clearOpenTimer() {
  if (openTimer === null) return;
  clearTimeout(openTimer);
  openTimer = null;
}
