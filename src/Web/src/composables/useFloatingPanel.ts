import { onUnmounted, ref, type Ref } from "vue";

export interface FloatingPanelGeometry {
  /** Below this much room underneath the anchor, the panel drops upward instead - but
   * only when there's genuinely more room above than below, so it never flips on a
   * nearly full screen. */
  flipThreshold: number;
  maxHeight: number;
  minWidth?: number;
}

/**
 * Positions a Teleport-to-body panel against its anchor element, fixed to the viewport
 * so it isn't clipped by an ancestor that scrolls its own content (a modal body, for
 * instance). Shared by RegionSelect and RegionFilterSelect, whose dropdowns differ only
 * in their sizing thresholds.
 */
export function useFloatingPanel(anchor: Ref<HTMLElement | null>, geometry: FloatingPanelGeometry) {
  const style = ref<Record<string, string>>({});

  function reposition() {
    const el = anchor.value;
    if (!el) return;

    const rect = el.getBoundingClientRect();
    const below = window.innerHeight - rect.bottom - 8;
    const above = rect.top - 8;
    const dropUp = below < geometry.flipThreshold && above > below;
    const width = geometry.minWidth ? Math.max(rect.width, geometry.minWidth) : rect.width;

    style.value = {
      left: `${rect.left}px`,
      width: `${width}px`,
      maxHeight: `${Math.min(geometry.maxHeight, dropUp ? above : below)}px`,
      ...(dropUp
        ? { bottom: `${window.innerHeight - rect.top + 4}px` }
        : { top: `${rect.bottom + 4}px` }),
    };
  }

  // Capture phase: the scrolling ancestor may not be the window, and that scroll
  // doesn't bubble.
  function track() {
    reposition();
    window.addEventListener("scroll", reposition, true);
    window.addEventListener("resize", reposition);
  }

  function untrack() {
    window.removeEventListener("scroll", reposition, true);
    window.removeEventListener("resize", reposition);
  }

  onUnmounted(untrack);

  return { style, reposition, track, untrack };
}
