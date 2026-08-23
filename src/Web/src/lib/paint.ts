/** Resolves once the browser has painted the frame currently pending, so work scheduled
 * after it can't hold that paint hostage. Two animation frames, not nextTick: nextTick
 * only flushes Vue's own patch, still inside the same microtask turn as the reactive
 * write that preceded it, before the browser has had any chance to paint. A single rAF
 * fires before its own frame's paint too - only a second, nested one is guaranteed to
 * run after the previous frame has actually reached the screen. */
export function afterPaint(): Promise<void> {
  return new Promise((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
  });
}
