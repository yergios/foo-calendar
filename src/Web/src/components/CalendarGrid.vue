<template>
  <div class="flex gap-1.5 flex-1 min-h-0">
    <button
      type="button"
      class="shrink-0 w-7 flex items-center justify-center rounded-8 hover:bg-surface text-primary-text text-lg transition-colors disabled:opacity-40 disabled:hover:bg-transparent disabled:cursor-not-allowed"
      aria-label="Scroll left"
      :disabled="!canScrollLeft"
      @click="handlePageClick(-1)"
      @mousedown="startHold(-1)"
      @mouseup="stopHold"
      @mouseleave="stopHold"
    >
      ‹
    </button>

    <!-- Every pointer interaction with the grid is resolved here from coordinates: the
         geometry is fixed (see lib/gridGeometry.ts), so which day and which row the mouse
         is over is arithmetic, not a hit test over thousands of elements. See locate() for
         the math and the comment on the overlays for why. The row height also goes to the
         CSS below as a custom property, for the stripe and row-line gradients. -->
    <div
      ref="containerRef"
      class="overflow-auto flex-1 min-w-0 border border-border rounded-12 shadow-card"
      :class="reorderable ? undefined : 'reorder-off'"
      :style="{ '--row-h': `${ROW_H}px` }"
      role="grid"
      :aria-rowcount="rows.length"
      :aria-colcount="days.length + 1"
      @mousemove="handleMouseMove"
      @mouseleave="handleMouseLeave"
      @scroll="handleScroll"
      @click="handleClick"
    >
      <div class="relative" :style="{ width: `${contentWidth}px` }">
        <div
          class="sticky top-0 z-20 bg-background"
          :style="{ height: `${HEADER_H}px` }"
          role="row"
        >
          <div
            class="sticky left-0 z-30 h-full bg-background border-b border-border shadow-[2px_0_0_0_var(--color-border)] flex items-center justify-center text-xs font-semibold text-secondary"
            :style="{ width: `${NAME_W}px` }"
            role="columnheader"
          >
            Team
          </div>

          <div
            v-for="group in monthGroups"
            :key="group.month"
            class="absolute top-0 border-l-2 border-b border-border text-center text-xs font-semibold text-ink bg-surface truncate"
            :style="{
              left: `${colX(group.start)}px`,
              width: `${group.span * COL_W}px`,
              height: `${MONTH_H}px`,
              lineHeight: `${MONTH_H}px`,
            }"
          >
            {{ group.label }}
          </div>

          <!-- Memo'd on the day's precomputed info, like the body columns below: the
               365 header cells are static within a year and must not be re-diffed on
               every scroll or reorder render of the grid. -->
          <div
            v-for="(day, i) in days"
            :key="day.iso"
            v-memo="[dayRenderInfo[i]]"
            class="absolute border-b border-border flex flex-col items-center justify-center text-xs"
            :class="dayRenderInfo[i]!.headerClass"
            :style="{
              left: `${colX(i)}px`,
              top: `${MONTH_H}px`,
              width: `${COL_W}px`,
              height: `${DAY_H}px`,
            }"
            :title="dayRenderInfo[i]!.headerTitle"
            role="columnheader"
          >
            <div class="font-semibold" :class="day.isToday ? 'text-today' : 'text-ink'">
              {{ day.dayNum }}
            </div>
            <div
              class="text-[10px]"
              :class="day.isToday ? 'text-today font-semibold' : 'text-secondary'"
            >
              {{ day.weekdayLabel }}
            </div>
          </div>

          <!-- Today's outline, header half; the body half is the overlay further down.
               Both are drawn from the same column geometry, on top of the column's own
               left divider, so the two halves can't drift apart. -->
          <div
            v-if="todayIdx >= 0"
            class="absolute border-l-2 border-r-2 border-t-2 border-today pointer-events-none"
            :style="{
              left: `${colX(todayIdx)}px`,
              top: `${MONTH_H}px`,
              width: `${COL_W}px`,
              height: `${DAY_H}px`,
            }"
          />
        </div>

        <div class="calendar-body relative" :style="{ height: `${bodyHeight}px` }">
          <!-- Day columns: the weekend/holiday tint and the day and month dividers, drawn
               once per day for the full height of the body rather than once per cell.
               Static for a given year and holiday set. -->
          <div
            v-for="(day, i) in days"
            :key="day.iso"
            v-memo="[dayRenderInfo[i]]"
            class="absolute top-0 bottom-0"
            :class="dayRenderInfo[i]!.columnClass"
            :style="{ left: `${colX(i)}px`, width: `${COL_W}px` }"
            :title="dayRenderInfo[i]!.holidayTitle || undefined"
          />
          <div class="calendar-lines absolute inset-0 pointer-events-none" />

          <!-- The name column. Sticky like the header's corner; carries the same zebra
               stripes as the body so it stays opaque over the day columns scrolling
               underneath it. Only the rows in the window exist, same as the grid rows. -->
          <div
            class="calendar-names sticky left-0 z-10 h-full shadow-[2px_0_0_0_var(--color-border)]"
            :style="{ width: `${NAME_W}px` }"
          >
            <template v-if="loading">
              <div
                v-for="n in skeletonRowCount"
                :key="`skeleton-${n}`"
                class="absolute top-0 left-0 w-full flex items-center px-2"
                :style="{ height: `${ROW_H}px`, transform: `translateY(${rowY(n - 1)}px)` }"
              >
                <AppSkeleton class="h-4 w-20" />
              </div>
            </template>
            <div
              v-for="item in visibleRows"
              v-else
              :key="item.person.id"
              class="calendar-name absolute top-0 left-0 w-full flex items-center gap-2 px-2 text-sm cursor-grab active:cursor-grabbing"
              :style="{ height: `${ROW_H}px`, transform: `translateY(${item.top}px)` }"
              draggable="true"
              role="rowheader"
              @dragstart="handleDragStart($event, item.person.id)"
              @dragover.prevent
              @drop="handleDrop(item.person.id)"
            >
              <!-- The tooltip sits on the grip rather than the whole cell so that
                   hiding the grip takes the promise with it: a cell that still offered
                   "drag to reorder" while a search had the drag disabled would be
                   advertising something the drop then silently ignores. -->
              <span
                class="drag-grip text-secondary/40 select-none text-xs leading-none shrink-0"
                title="Drag to reorder"
                >⠿</span
              >
              <span
                class="truncate min-w-0"
                :class="item.isMine ? 'text-primary-text font-semibold' : 'text-ink'"
                >{{ item.person.displayName ?? item.person.username }}</span
              >
              <span
                v-if="item.regionCode"
                class="ml-auto text-[9px] text-secondary shrink-0"
                :title="item.regionLabel ?? undefined"
                >{{ item.regionCode }}</span
              >
            </div>
          </div>

          <template v-if="loading">
            <div
              v-for="n in skeletonRowCount"
              :key="`skeleton-${n}`"
              class="absolute top-0 right-0 p-1.5"
              :style="{
                left: `${NAME_W}px`,
                height: `${ROW_H}px`,
                transform: `translateY(${rowY(n - 1)}px)`,
              }"
            >
              <AppSkeleton class="h-full w-full" />
            </div>
          </template>

          <!-- One row per visible person, positioned by transform so that a reorder is a
               style change on the rows in view and nothing else: no DOM moves, no layout.
               The segments inside (one element per absence run, see
               lib/absenceSegments.ts, rather than one per day) live in a child component
               whose props don't change on a reorder, so Vue skips them entirely. -->
          <div
            v-for="item in visibleRows"
            v-else
            :key="item.person.id"
            v-memo="[item.person, item.segments, item.top, item.isMine, configStore.config]"
            class="calendar-row absolute top-0 left-0 right-0"
            :style="{ height: `${ROW_H}px`, transform: `translateY(${item.top}px)` }"
            role="row"
          >
            <CalendarGridRow
              :person="item.person"
              :segments="item.segments"
              :is-mine="item.isMine"
              :home-time-zone="configStore.config?.homeTimeZone"
            />
          </div>

          <!-- Overlays. Today's column, the viewer's own row and the hover highlight are
               each one element placed by a transform, so showing or moving them touches
               nothing underneath: no cell is re-rendered, re-styled or repainted.
               Stacking is DOM order, except the name column (sticky, z-10) and the two
               row-wide overlays that have to sit above it (z-11). -->
          <div
            v-if="todayIdx >= 0"
            class="absolute top-0 bottom-0 border-l-2 border-r-2 border-b-2 border-today pointer-events-none"
            :style="{ left: `${colX(todayIdx)}px`, width: `${COL_W}px` }"
          />
          <div
            ref="hoverColRef"
            class="calendar-hover absolute top-0 bottom-0 left-0 pointer-events-none"
            :style="{ width: `${COL_W}px` }"
            hidden
          />
          <!-- The one overlay that takes pointer events: it is what sits under the cursor,
               so it carries the tooltip (title) and cursor of whatever cell is hovered. -->
          <div
            ref="hoverCellRef"
            class="absolute top-0 left-0"
            :style="{ width: `${COL_W}px`, height: `${ROW_H}px` }"
            hidden
          />
          <div
            v-if="!loading && mineTop !== null"
            class="calendar-mine absolute left-0 right-0 top-0 z-11 border-2 border-primary-text pointer-events-none"
            :style="{ height: `${ROW_H}px`, transform: `translateY(${mineTop}px)` }"
          />
          <div
            ref="hoverRowRef"
            class="calendar-hover absolute left-0 right-0 top-0 z-11 pointer-events-none"
            :style="{ height: `${ROW_H}px` }"
            hidden
          />
        </div>
      </div>
    </div>

    <button
      type="button"
      class="shrink-0 w-7 flex items-center justify-center rounded-8 hover:bg-surface text-primary-text text-lg transition-colors disabled:opacity-40 disabled:hover:bg-transparent disabled:cursor-not-allowed"
      aria-label="Scroll right"
      :disabled="!canScrollRight"
      @click="handlePageClick(1)"
      @mousedown="startHold(1)"
      @mouseup="stopHold"
      @mouseleave="stopHold"
    >
      ›
    </button>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";

import AppSkeleton from "@/components/AppSkeleton.vue";
import CalendarGridRow from "@/components/CalendarGridRow.vue";
import {
  type AbsenceSegment,
  absenceTooltip,
  dayIndexOf,
  segmentAt,
  segmentsForPerson,
} from "@/lib/absenceSegments";
import { toIso, today } from "@/lib/dates";
import { COL_W, DAY_H, HEADER_H, MONTH_H, NAME_W, ROW_H, colX, rowY } from "@/lib/gridGeometry";
import { sameList } from "@/lib/reconcile";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import { usePersonStore } from "@/stores/person";
import type { AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

const props = defineProps<{
  /** Filtered but not sorted - see `order` below for display sequence. */
  people: PersonDto[];
  /** Person ids in the order rows should render. Kept separate from `people` so that a
   * sort-mode change (reordering only) doesn't invalidate the per-person segment
   * computation below, which only cares about *which* people are visible, not what order
   * they're in. */
  order: number[];
  absences: AbsenceDto[];
  holidays: BankHolidayDto[];
  year: number;
  loading: boolean;
  /** Whether rows can be dragged into a new order. False while something other than the
   * sort mode decides the order - a live search ranks by match score, so a drop would
   * write a custom order the grid then refuses to show. */
  reorderable: boolean;
}>();

const emit = defineEmits<{
  "add-absence": [date: string];
  "edit-absence": [absences: AbsenceDto[], date: string];
  "edit-holiday": [holiday: BankHolidayDto];
  "add-holiday": [date: string];
  reorder: [personId: number, targetPersonId: number];
}>();

// Rows rendered beyond the visible ones on each side, so a scroll of a few rows lands on
// rows that already exist and the window only has to catch up, not race the frame.
const OVERSCAN = 5;

const configStore = useConfigStore();
const personStore = usePersonStore();
const { regionLabels: peopleRegionLabels } = storeToRefs(usePeopleStore());
const containerRef = ref<HTMLDivElement | null>(null);
const hoverColRef = ref<HTMLDivElement | null>(null);
const hoverRowRef = ref<HTMLDivElement | null>(null);
const hoverCellRef = ref<HTMLDivElement | null>(null);

// Native HTML5 drag-and-drop rather than a library: reordering a handful of rows
// doesn't need anything fancier, and it keeps this already-heavy component lean.
//
// Both handlers read props.reorderable at call time rather than the template gating the
// `draggable` attribute on it, so a search starting or ending doesn't re-render every
// name cell just to flip an attribute. The affordances (cursor and grip) are turned off in
// CSS instead, see .reorder-off at the bottom of this file.
let draggedPersonId: number | null = null;

function handleDragStart(event: DragEvent, personId: number) {
  if (!props.reorderable) {
    event.preventDefault();
    return;
  }
  draggedPersonId = personId;
}

function handleDrop(targetPersonId: number) {
  if (!props.reorderable) return;
  if (draggedPersonId !== null && draggedPersonId !== targetPersonId) {
    emit("reorder", draggedPersonId, targetPersonId);
  }
  draggedPersonId = null;
}

// ---- Pointer geometry ------------------------------------------------------------------

interface GridPoint {
  /** Index into `days`, or -1 when the pointer is over the name column or past the year. */
  dayIdx: number;
  /** Index into `rows`, or -1 when the pointer is over the header or below the last row. */
  rowIdx: number;
  /** True over the day-header band only (month labels above it carry no click/hover of
   * their own, same as before this rewrite). */
  inDayHeader: boolean;
}

// Where a viewport point falls in the grid. The header and the name column are sticky,
// so the visible box is split first: anything within HEADER_H of the top is the header
// whatever the scroll position, anything within NAME_W of the left is a name.
function locate(clientX: number, clientY: number): GridPoint | null {
  // Rows and their segments aren't rendered while loading (see the template), and the
  // skeleton beneath is a placeholder, not clickable content - hover and click both no-op.
  if (props.loading) return null;
  const container = containerRef.value;
  if (!container) return null;
  const rect = container.getBoundingClientRect();
  const x = clientX - rect.left - container.clientLeft;
  const y = clientY - rect.top - container.clientTop;
  if (x < 0 || y < 0 || x >= container.clientWidth || y >= container.clientHeight) return null;

  const inDayHeader = y >= MONTH_H && y < HEADER_H;
  let dayIdx = x < NAME_W ? -1 : Math.floor((x + container.scrollLeft - NAME_W) / COL_W);
  if (dayIdx >= days.value.length) dayIdx = -1;
  let rowIdx = y < HEADER_H ? -1 : Math.floor((y + container.scrollTop - HEADER_H) / ROW_H);
  if (rowIdx >= rows.value.length) rowIdx = -1;
  return { dayIdx, rowIdx, inDayHeader };
}

// The hover highlight is applied by moving three overlay elements, not by touching any
// cell: nothing under them is re-rendered, re-styled or repainted. State lives in plain
// variables rather than refs for the same reason - routing it through Vue would schedule
// a render for something the DOM is already showing.
let hoverDay = -1;
let hoverRow = -1;
let hoverTitle = "";
let hoverPointer = false;
// The last pointer position, replayed after a scroll so the highlight follows the content
// moving under a still cursor.
let lastClientX = Number.NaN;
let lastClientY = Number.NaN;

function setHover(point: GridPoint | null) {
  const dayIdx = point?.dayIdx ?? -1;
  const rowIdx = point?.rowIdx ?? -1;
  const col = hoverColRef.value;
  const row = hoverRowRef.value;
  const cell = hoverCellRef.value;
  if (!col || !row || !cell) return;

  if (dayIdx !== hoverDay) {
    col.hidden = dayIdx < 0;
    if (dayIdx >= 0) col.style.transform = `translateX(${colX(dayIdx)}px)`;
  }
  if (rowIdx !== hoverRow) {
    row.hidden = rowIdx < 0;
    if (rowIdx >= 0) row.style.transform = `translateY(${rowY(rowIdx)}px)`;
  }
  if (dayIdx !== hoverDay || rowIdx !== hoverRow) {
    const over = dayIdx >= 0 && rowIdx >= 0;
    cell.hidden = !over;
    if (over) {
      cell.style.transform = `translate(${colX(dayIdx)}px, ${rowY(rowIdx)}px)`;
      const item = rows.value[rowIdx]!;
      const segment = segmentAt(segmentsFor(item.person.id), dayIdx);
      const title = segment
        ? absenceTooltip(
            item.person,
            segment.absences,
            item.isMine,
            configStore.config?.homeTimeZone,
          )
        : emptyCellTitle(item.isMine, dayRenderInfo.value[dayIdx]!.holidayTitle);
      if (title !== hoverTitle) {
        hoverTitle = title;
        if (title) cell.title = title;
        else cell.removeAttribute("title");
      }
      if (item.isMine !== hoverPointer) {
        hoverPointer = item.isMine;
        cell.classList.toggle("cursor-pointer", item.isMine);
      }
    }
    hoverDay = dayIdx;
    hoverRow = rowIdx;
  }
}

function handleMouseMove(event: MouseEvent) {
  lastClientX = event.clientX;
  lastClientY = event.clientY;
  setHover(locate(event.clientX, event.clientY));
}

function handleMouseLeave() {
  lastClientX = Number.NaN;
  lastClientY = Number.NaN;
  setHover(null);
}

// One handler for the whole grid, resolved from coordinates. A day stacking several
// time-based absences needs one unambiguous click target rather than "click the one
// letter currently shown", so the whole cell is clickable for the viewer's own row
// regardless of what it holds. Non-owned rows stay non-clickable (tooltip only).
function handleClick(event: MouseEvent) {
  const point = locate(event.clientX, event.clientY);
  if (!point || point.dayIdx < 0) return;
  const day = days.value[point.dayIdx]!;
  if (point.inDayHeader) {
    handleHeaderClick(day);
    return;
  }
  if (point.rowIdx < 0) return;
  const item = rows.value[point.rowIdx]!;
  if (!item.isMine) return;
  const segment = segmentAt(segmentsFor(item.person.id), point.dayIdx);
  if (segment) emit("edit-absence", segment.absences, day.iso);
  else emit("add-absence", day.iso);
}

function handleScroll() {
  updateScrollButtons();
  updateWindow();
  if (!Number.isNaN(lastClientX)) setHover(locate(lastClientX, lastClientY));
}

onMounted(() => {
  configStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load region catalog:", error);
  });
  scrollToToday();

  updateRowCapacity();
  updateWindow();
  updateScrollButtons();
  if (containerRef.value) {
    resizeObserver = new ResizeObserver(scheduleRowCapacityUpdate);
    resizeObserver.observe(containerRef.value);
  }
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  stopHold();
});

/** Scrolls the grid so today's column is centered in the day area. No-op if today isn't in the displayed year. */
async function scrollToToday() {
  await nextTick();
  const container = containerRef.value;
  if (!container || todayIdx.value < 0) return;
  container.scrollLeft =
    colX(todayIdx.value) - NAME_W + COL_W / 2 - (container.clientWidth - NAME_W) / 2;
}

defineExpose({ scrollToToday });

// Lets the arrow buttons stand in for the horizontal scrollbar, which a year's worth of
// day columns pushes to the very bottom of the page - out of the way of the grid itself,
// and easy to miss. Scrolls by nearly a full viewport width rather than a fixed number of
// days, so a click always advances a useful amount regardless of window size; the 10%
// overlap leaves a sliver of the previous view in frame as an anchor.
const canScrollLeft = ref(false);
const canScrollRight = ref(false);

function updateScrollButtons() {
  const container = containerRef.value;
  if (!container) return;
  canScrollLeft.value = container.scrollLeft > 0;
  // A pixel of slack: without it, subpixel scroll positions can leave this true forever,
  // a hair short of the max scrollLeft a browser actually reports at full scroll.
  canScrollRight.value = container.scrollLeft < container.scrollWidth - container.clientWidth - 1;
}

function scrollByPage(direction: 1 | -1) {
  const container = containerRef.value;
  if (!container) return;
  container.scrollBy({ left: direction * container.clientWidth * 0.9, behavior: "smooth" });
}

// A plain click still does the one-page jump above (bound via @click, so it also covers
// keyboard activation); holding the button down instead ramps into a continuous scroll,
// like an OS scrollbar's own arrow buttons. The delay before it kicks in is what stops an
// ordinary click's mousedown from also starting one - mouseup cancels the pending timeout
// well before it'd fire.
const HOLD_DELAY_MS = 350;
const HOLD_SPEED_PX_PER_FRAME = 14;
let holdTimeout: ReturnType<typeof setTimeout> | null = null;
let holdFrame: number | null = null;
// Set once a hold actually starts scrolling, so the click that follows releasing the
// button - mouseup fires stopHold first, then the browser dispatches its own click event
// after - can tell it's just the tail end of that hold, not a fresh press, and skip
// paging a second time.
let holdEngaged = false;

function startHold(direction: 1 | -1) {
  stopHold();
  holdEngaged = false;
  holdTimeout = setTimeout(() => {
    holdEngaged = true;
    const step = () => {
      const container = containerRef.value;
      // Stops itself at either end rather than spinning uselessly against the clamped
      // scroll position until the button is finally released.
      if (!container || (direction === 1 ? !canScrollRight.value : !canScrollLeft.value)) return;
      container.scrollLeft += direction * HOLD_SPEED_PX_PER_FRAME;
      holdFrame = requestAnimationFrame(step);
    };
    holdFrame = requestAnimationFrame(step);
  }, HOLD_DELAY_MS);
}

function stopHold() {
  if (holdTimeout !== null) {
    clearTimeout(holdTimeout);
    holdTimeout = null;
  }
  if (holdFrame !== null) {
    cancelAnimationFrame(holdFrame);
    holdFrame = null;
  }
}

function handlePageClick(direction: 1 | -1) {
  if (holdEngaged) {
    holdEngaged = false;
    return;
  }
  scrollByPage(direction);
}

// ---- Days and columns ------------------------------------------------------------------

// region/country code -> display name, e.g. "CH-ZH" -> "Zürich", "CH" -> "Switzerland".
// Comes from the people payload rather than config: it covers wherever the team is now,
// which is also the only place holidays are stored for. The people store's
// reconcilePeople keeps the labels object's identity across a refetch that changed
// nothing, so this only ever recomputes when the labels genuinely changed.
const regionLabels = computed(() => new Map(Object.entries(peopleRegionLabels.value)));

const days = computed(() => {
  const weekdayFmt = new Intl.DateTimeFormat(navigator.language, { weekday: "short" });
  // Only 7 distinct labels exist, so format each weekday once and index by getDay()
  // instead of paying an Intl format call per day. January 2023 starts on a Sunday,
  // matching getDay()'s Sunday-is-0 indexing.
  const weekdayLabels = Array.from({ length: 7 }, (_, weekday) =>
    weekdayFmt.format(new Date(2023, 0, 1 + weekday)),
  );
  // Midday, not midnight: where a DST jump lands on 00:00 (America/Santiago, Asia/Beirut)
  // that midnight doesn't exist, so `d` gets pushed to 01:00 and setDate keeps it there for
  // the rest of the year. Against a midnight `end` that silently drops December 31.
  const end = new Date(props.year, 11, 31, 12);
  const todayIso = today();

  const result = [];
  for (const d = new Date(props.year, 0, 1); d <= end; d.setDate(d.getDate() + 1)) {
    const iso = toIso(d);
    result.push({
      iso,
      month: d.getMonth(),
      dayNum: d.getDate(),
      isWeekend: d.getDay() === 0 || d.getDay() === 6,
      isToday: iso === todayIso,
      weekdayLabel: weekdayLabels[d.getDay()],
    });
  }
  return result;
});

const dayIndex = computed(() => dayIndexOf(days.value));
const todayIdx = computed(() => days.value.findIndex((day) => day.isToday));
const contentWidth = computed(() => NAME_W + days.value.length * COL_W);

const monthGroups = computed(() => {
  const fmt = new Intl.DateTimeFormat(navigator.language, { month: "long" });
  const groups: { month: number; label: string; start: number; span: number }[] = [];

  days.value.forEach((day, index) => {
    const current = groups[groups.length - 1];
    if (current?.month === day.month) {
      current.span++;
    } else {
      groups.push({
        month: day.month,
        label: fmt.format(new Date(props.year, day.month, 1)),
        start: index,
        span: 1,
      });
    }
  });

  return groups;
});

const holidayMap = computed(() => {
  const map = new Map<string, BankHolidayDto[]>();
  for (const h of props.holidays) {
    const entries = map.get(h.date) ?? [];
    entries.push(h);
    map.set(h.date, entries);
  }
  return map;
});

// Everything a day contributes to the rendering - the weekend/holiday background, the
// month-boundary side border, the header's tooltip and click affordance - resolved once
// per day here rather than per row or per render. Recomputed only when the year, the
// holidays or the region labels change; the header cells and body columns memo on each
// day's object, so a render of the grid for any other reason never revisits the 365 days.
const dayRenderInfo = computed(() =>
  days.value.map((day) => {
    const clickable = editableHoliday(day.iso) || canAddHoliday(day.iso);
    return {
      headerClass:
        `${dayHeaderClass(day, "bg-background")} ${columnSideBorderClass(day)}` +
        (clickable ? " cursor-pointer hover:opacity-70" : ""),
      headerTitle: headerTitle(day),
      columnClass: `${dayHeaderClass(day)} ${columnSideBorderClass(day)}`,
      holidayTitle: holidayName(day.iso),
    };
  }),
);

// ---- Rows and segments -----------------------------------------------------------------

// personId -> that person's absences, flat and in props order. Each person's array keeps
// its previous identity while its contents are element-identical. That works because
// HomeView never mutates DTOs in place: a save patches in one replaced object, and a
// background refetch reconciles unchanged DTOs back to their previous objects (see
// lib/reconcile.ts). Identity per person's array is what the segment cache below
// compares, so this is what makes one changed absence rebuild one row.
let prevAbsencesByPerson = new Map<number, AbsenceDto[]>();

const absencesByPerson = computed(() => {
  const grouped = new Map<number, AbsenceDto[]>();
  for (const a of props.absences) {
    const list = grouped.get(a.personId);
    if (list) list.push(a);
    else grouped.set(a.personId, [a]);
  }
  for (const [personId, list] of grouped) {
    const prev = prevAbsencesByPerson.get(personId);
    if (prev && sameList(prev, list)) grouped.set(personId, prev);
  }
  prevAbsencesByPerson = grouped;
  return grouped;
});

const NO_SEGMENTS: AbsenceSegment[] = [];

// Segments are cached per person and computed on demand, so only the people in the
// window ever pay for theirs, and a person keeps the same segments array - which the row
// v-memo compares - while the absences it was built from match by identity (see
// absencesByPerson). Nothing else feeds it: holidays don't affect painting, and a changed
// name or region can't stale an entry. Entries for people currently filtered out or
// scrolled away are kept; the ceiling is the whole roster, which is what's in memory
// anyway. A year change swaps the days every entry was built against, so it drops the
// whole cache - which also stops it pinning the previous year's absences.
interface SegmentCacheEntry {
  segments: AbsenceSegment[];
  /** undefined when the person had no absences at all - still a valid, comparable input. */
  sourceAbsences: AbsenceDto[] | undefined;
}
const segmentCache = new Map<number, SegmentCacheEntry>();
watch(days, () => segmentCache.clear(), { flush: "sync" });

function segmentsFor(personId: number): AbsenceSegment[] {
  const source = absencesByPerson.value.get(personId);
  const entry = segmentCache.get(personId);
  if (entry && entry.sourceAbsences === source) return entry.segments;
  const segments = source ? segmentsForPerson(source, days.value, dayIndex.value) : NO_SEGMENTS;
  segmentCache.set(personId, { segments, sourceAbsences: source });
  return segments;
}

const personById = computed(() => new Map(props.people.map((person) => [person.id, person])));

// Every row in display order, without their segments: index math and two map lookups per
// person, so a sort-mode change stays cheap no matter how big the team is. Which of these
// are actually rendered is decided by the window below.
const rows = computed(() => {
  const myId = personStore.person?.id;
  return props.order.map((personId) => {
    const person = personById.value.get(personId)!;
    return {
      person,
      isMine: person.id === myId,
      // Code shown on the row itself (compact enough for the widened name column); the
      // full label is only surfaced on hover, via the title.
      regionCode: person.homeCity,
      regionLabel: person.homeCity
        ? (regionLabels.value.get(person.homeCity) ?? person.homeCity)
        : null,
    };
  });
});

// The rows that exist in the DOM: those within the scrolled-to part of the body, plus
// OVERSCAN either side. Everything that scales with the team - style, layout, paint, hit
// testing - scales with this instead, which is bounded by the viewport.
const windowStart = ref(0);
const windowEnd = ref(0);

function updateWindow() {
  const container = containerRef.value;
  if (!container) return;
  const first = Math.floor(container.scrollTop / ROW_H);
  const last = Math.ceil((container.scrollTop + container.clientHeight - HEADER_H) / ROW_H);
  // The overscan is slack, not a margin to maintain: as long as every visible row is
  // already rendered the window stays put, so a scroll re-renders nothing until it has
  // eaten through the overscan on one side. Then the window jumps to re-centre.
  if (first >= windowStart.value && last <= windowEnd.value) return;
  windowStart.value = first - OVERSCAN;
  windowEnd.value = last + OVERSCAN;
}

// Handed to the template in person-id order, not display order: a row's place on screen
// is its transform, so DOM order is free to stay put, and a fixed order is what keeps the
// keyed v-for from moving row elements around to mirror a sort. With it, a reorder
// changes 27 transforms; without it, Vue would also re-insert 27 rows of segments.
const visibleRows = computed(() => {
  const all = rows.value;
  const start = Math.max(0, windowStart.value);
  const end = Math.min(all.length - 1, windowEnd.value);
  const result = [];
  for (let i = start; i <= end; i++) {
    const row = all[i]!;
    result.push({ ...row, segments: segmentsFor(row.person.id), top: rowY(i) });
  }
  return result.sort((a, b) => a.person.id - b.person.id);
});

const mineTop = computed(() => {
  const index = rows.value.findIndex((row) => row.isMine);
  return index === -1 ? null : rowY(index);
});

// The hover overlays and tooltip are set imperatively from mouse/scroll events (see
// setHover), so they don't react on their own when the data under a stationary pointer
// changes: a search narrowing `rows`, or a save/delete changing which absences a hovered
// cell holds. This replays the last pointer position whenever either can have changed,
// forcing setHover past its "nothing moved" short-circuit so the title and highlight
// catch up. Doesn't fire on every keystroke's grid update alone - only when the set of
// rows or their absences actually changes.
watch([rows, () => props.absences], () => {
  if (Number.isNaN(lastClientX)) return;
  hoverDay = Number.NaN;
  hoverRow = Number.NaN;
  setHover(locate(lastClientX, lastClientY));
});

// How many rows fit in the visible container. Backs the loading skeleton's row count
// (filling the same space, since a team's size isn't known yet while it loads) and the
// body's minimum height, so a small team's stripes and day columns still run to the
// bottom of the container instead of stopping at the last person.
const rowCapacity = ref(0);
let resizeObserver: ResizeObserver | null = null;
let measureScheduled = false;

function updateRowCapacity() {
  const container = containerRef.value;
  if (!container) return;
  rowCapacity.value = Math.max(0, Math.floor((container.clientHeight - HEADER_H) / ROW_H));
}

// Coalesces bursts of resize callbacks (a window drag-resize fires many a second) down to
// one measurement per animation frame, run after the current task rather than inline so
// the read doesn't force a layout in the middle of whatever triggered it.
function scheduleRowCapacityUpdate() {
  if (measureScheduled) return;
  measureScheduled = true;
  requestAnimationFrame(() => {
    measureScheduled = false;
    updateRowCapacity();
    updateWindow();
    // Piggybacks on the same pass: a year change resizes the content (and so scrollWidth)
    // without necessarily moving scrollLeft, which the scroll listener alone wouldn't catch.
    updateScrollButtons();
  });
}

watch(() => props.year, scheduleRowCapacityUpdate);

// At least one row even before the first measurement lands, so the skeleton never
// flashes empty.
const skeletonRowCount = computed(() => Math.max(1, rowCapacity.value));

const bodyHeight = computed(
  () =>
    Math.max(props.loading ? skeletonRowCount.value : rows.value.length, rowCapacity.value) * ROW_H,
);

// ---- Styling helpers -------------------------------------------------------------------

// Weekends and holidays keep their own opaque tint regardless of row, since those already
// mark the column and shouldn't be muddied by the row's own background. Everywhere else
// this returns nothing (plainBackground's default): the column stays transparent and lets
// the body's zebra stripes (see the CSS at the bottom of this file) show through.
//
// The day header is the one caller that wants an explicit opaque background of its own
// (plainBackground passed as 'bg-background'): it's sticky and has content scrolling
// underneath it.
function dayHeaderClass(day: { iso: string; isWeekend: boolean }, plainBackground = "") {
  if (holidayMap.value.has(day.iso)) return "bg-holiday";
  if (day.isWeekend) return "bg-surface";
  return plainBackground;
}

// Every day gets a real left border: the column-axis equivalent of the row lines, and
// using left rather than right avoids doubling up with the month-boundary border (each
// divider has exactly one owner, the column to its right). Month starts get a thicker,
// stronger-colored version so the grouping still stands out from the day-to-day lines.
// Today's outline (see the template) is drawn over its column's divider, so today needs
// no special case here.
function columnSideBorderClass(day: { dayNum: number }): string {
  return day.dayNum === 1 ? "border-l-2 border-l-border" : "border-l border-border";
}

function holidayName(iso: string): string {
  const entries = holidayMap.value.get(iso);
  if (!entries) return "";
  return entries
    .map((e) => `${e.name} (${regionLabels.value.get(e.region) ?? e.region})`)
    .join(", ");
}

// Only offered when there's exactly one manually-added holiday on that day, so a click
// never has to guess which of several overlapping holidays you meant to edit.
function editableHoliday(iso: string): BankHolidayDto | undefined {
  const entries = holidayMap.value.get(iso);
  if (!entries || entries.length !== 1) return undefined;
  return entries[0]!.source === "Manual" ? entries[0] : undefined;
}

// A day with no holiday at all can be clicked to add one. A day already carrying a
// Nager-sourced or multiple holidays is left alone, same as editableHoliday above.
function canAddHoliday(iso: string): boolean {
  return !holidayMap.value.has(iso);
}

function headerTitle(day: { iso: string }): string {
  const holiday = holidayName(day.iso);
  if (editableHoliday(day.iso)) return `${holiday} (click to edit)`;
  if (canAddHoliday(day.iso)) return "Click to add bank holiday";
  return holiday;
}

function handleHeaderClick(day: { iso: string }) {
  const holiday = editableHoliday(day.iso);
  if (holiday) {
    emit("edit-holiday", holiday);
    return;
  }
  if (canAddHoliday(day.iso)) {
    emit("add-holiday", day.iso);
  }
}

function emptyCellTitle(isMine: boolean, holidayTitle: string): string {
  if (!isMine) return holidayTitle;
  return holidayTitle ? `${holidayTitle} (click to add absence)` : "Click to add absence";
}
</script>

<style scoped>
/* Zebra striping is a repeating gradient rather than a background per row: rows are
   positioned by transform at multiples of the row height (--row-h, set on the scroller
   from lib/gridGeometry.ts), so the stripe under row n is the band at n * --row-h
   whatever order the rows are in. The name column repeats it so it stays opaque over the
   day columns scrolling underneath. */
.calendar-body,
.calendar-names {
  background: repeating-linear-gradient(
    to bottom,
    var(--color-background) 0 var(--row-h),
    var(--color-row-alt) var(--row-h) calc(var(--row-h) * 2)
  );
}

/* Row dividers, same trick: one gradient over the whole body instead of a border on every
   cell. Sits above the day columns so a weekend's tint doesn't hide the line. */
.calendar-lines {
  background: repeating-linear-gradient(
    to bottom,
    transparent 0 calc(var(--row-h) - 1px),
    var(--color-border) calc(var(--row-h) - 1px) var(--row-h)
  );
}

/* Size, layout and paint containment: a row's box is known (full width, fixed height), so
   the browser never has to look inside one row to lay out or paint another. */
.calendar-row {
  contain: strict;
}

.calendar-hover {
  background: color-mix(in srgb, var(--color-primary-text) 10%, transparent);
}

.calendar-mine {
  background: color-mix(in srgb, var(--color-primary) 8%, transparent);
}

/* Name cells keep their `draggable` attribute and their grab cursor when reordering is
   off (see handleDragStart's comment). Both affordances are withdrawn here instead, where
   the browser applies them to the existing DOM without Vue patching anything. visibility
   rather than display for the grip, so the name column doesn't reflow as you start and
   stop typing - and, unlike opacity, it also takes the grip out of hit-testing, so its
   "Drag to reorder" tooltip goes quiet with it. */
.reorder-off .calendar-name {
  cursor: default;
}

.reorder-off .drag-grip {
  visibility: hidden;
}
</style>
