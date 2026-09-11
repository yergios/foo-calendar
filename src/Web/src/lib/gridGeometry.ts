/**
 * The calendar grid's geometry, in CSS pixels. Everything about where things are on
 * screen - what the mouse is over, where a row or a segment goes, how far to scroll for
 * today - is derived from these, never measured off the DOM. Both grid components read
 * them from here, and the grid's CSS gets them as custom properties set on the scroller.
 */

/** The name column's width is the one piece of this geometry the user controls, by
 * dragging its right edge. It lives in the --name-w custom property on the scroller
 * rather than in a constant or in reactive state, and every day position below is written
 * relative to it: a drag is then one variable write on one element, and the memoized day
 * columns, rows and segments are never re-diffed. CalendarGrid owns the current value. */
export const NAME_W_DEFAULT = 220;
export const NAME_W_MIN = 120;
export const NAME_W_MAX = 520;

export const COL_W = 28;
export const ROW_H = 36;
export const MONTH_H = 24;
export const DAY_H = 36;
export const HEADER_H = MONTH_H + DAY_H;
/** Width of a day column's left divider and of the line under each row. A month start
 * draws a 2px divider, which a segment starting there overlaps by a pixel. */
export const DIVIDER_W = 1;

/** Left edge of a day column, relative to the start of the day area. */
export const dayX = (dayIdx: number) => dayIdx * COL_W;
/** A point `x` pixels into the day area, as a CSS length that follows the name column's
 * width. Usable anywhere a length is: `left`, `width`, inside a `translate()`. */
export const dayAreaCss = (x: number) => `calc(var(--name-w) + ${x}px)`;
/** Top edge of a row within the grid's body. */
export const rowY = (rowIdx: number) => rowIdx * ROW_H;
