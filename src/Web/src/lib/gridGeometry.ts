/**
 * The calendar grid's geometry, in CSS pixels. Everything about where things are on
 * screen - what the mouse is over, where a row or a segment goes, how far to scroll for
 * today - is derived from these, never measured off the DOM. Both grid components read
 * them from here, and the grid's CSS gets them as custom properties set on the scroller.
 */
export const NAME_W = 160;
export const COL_W = 28;
export const ROW_H = 36;
export const MONTH_H = 24;
export const DAY_H = 36;
export const HEADER_H = MONTH_H + DAY_H;
/** Width of a day column's left divider and of the line under each row. A month start
 * draws a 2px divider, which a segment starting there overlaps by a pixel. */
export const DIVIDER_W = 1;

/** Left edge of a day column within the grid's content box. */
export const colX = (dayIdx: number) => NAME_W + dayIdx * COL_W;
/** Top edge of a row within the grid's body. */
export const rowY = (rowIdx: number) => rowIdx * ROW_H;
