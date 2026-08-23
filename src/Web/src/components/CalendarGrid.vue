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

    <div
      ref="containerRef"
      class="overflow-auto flex-1 min-w-0 border border-border rounded-12 shadow-card"
      @mouseover="handleGridHover"
      @mouseleave="clearHoverColumn"
      @scroll="updateScrollButtons"
    >
      <table
        class="w-full table-fixed border-separate border-spacing-0"
        :style="{ minWidth: tableMinWidth }"
      >
        <colgroup>
          <col style="width: 160px" />
          <col v-for="day in days" :key="day.iso" />
        </colgroup>

        <thead ref="theadRef" class="sticky top-0 z-20">
          <tr>
            <th
              scope="col"
              rowspan="2"
              class="sticky left-0 bg-background z-30 border-b border-border shadow-[2px_0_0_0_var(--color-border)] px-2 py-1 text-xs font-semibold text-secondary text-center align-middle"
            >
              Team
            </th>

            <th
              v-for="group in monthGroups"
              :key="group.month"
              scope="colgroup"
              :colspan="group.span"
              class="border-b border-l-2 border-border px-1 py-1 text-center text-xs font-semibold text-ink bg-surface"
            >
              {{ group.label }}
            </th>
          </tr>

          <tr>
            <th
              v-for="day in days"
              :key="day.iso"
              scope="col"
              class="border-b border-border px-1 py-1 text-center text-xs font-normal align-middle"
              :class="[
                dayHeaderClass(day, 'bg-background'),
                columnSideBorderClass(day),
                todayColumnClass(day.isToday, 'top'),
                editableHoliday(day.iso) || canAddHoliday(day.iso)
                  ? 'cursor-pointer hover:opacity-70'
                  : '',
              ]"
              :title="headerTitle(day)"
              :data-today="day.isToday || undefined"
              :data-iso="day.iso"
              @click="handleHeaderClick(day)"
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
            </th>
          </tr>
        </thead>

        <tbody v-if="loading">
          <tr v-for="n in skeletonRowCount" :key="`skeleton-${n}`">
            <th
              scope="row"
              class="sticky left-0 z-10 border-b border-border shadow-[2px_0_0_0_var(--color-border)] px-2 py-1.5 align-middle"
            >
              <AppSkeleton class="h-4 w-20" />
            </th>
            <td :colspan="days.length" class="border-b border-border h-9 p-1.5 align-middle">
              <AppSkeleton class="h-full w-full" />
            </td>
          </tr>
        </tbody>

        <tbody v-else>
          <tr
            v-for="row in rows"
            :key="row.person.id"
            v-memo="[
              row.person,
              row.cells,
              row.isMine,
              row.person.id === lastRowPersonId,
              regionLabels,
            ]"
            :data-person-id="row.person.id"
            :class="row.isMine ? 'bg-row-mine' : undefined"
          >
            <th
              scope="row"
              class="sticky left-0 z-10 border-b border-border shadow-[2px_0_0_0_var(--color-border)] px-2 py-1.5 text-sm font-normal text-left align-middle cursor-grab active:cursor-grabbing"
              :class="mineRowClass(row.isMine, 'start')"
              draggable="true"
              title="Drag to reorder"
              @dragstart="handleDragStart(row.person.id)"
              @dragover.prevent
              @drop="handleDrop(row.person.id)"
            >
              <div class="flex items-center gap-2">
                <span class="text-secondary/40 select-none text-xs leading-none shrink-0">⠿</span>
                <span
                  class="truncate min-w-0"
                  :class="row.isMine ? 'text-primary-text font-semibold' : 'text-ink'"
                  >{{ row.person.displayName ?? row.person.username }}</span
                >
                <span
                  v-if="row.regionCode"
                  class="ml-auto text-[9px] text-secondary shrink-0"
                  :title="row.regionLabel ?? undefined"
                  >{{ row.regionCode }}</span
                >
              </div>
            </th>

            <td
              v-for="(cell, cellIndex) in row.cells"
              :key="cell.day.iso"
              class="relative h-9 p-0 align-middle border-b border-border"
              :class="[
                cell.info.bodyClass,
                todayColumnClass(
                  cell.day.isToday,
                  row.person.id === lastRowPersonId ? 'bottom' : 'middle',
                ),
                mineRowClass(row.isMine, cellIndex === row.cells.length - 1 ? 'end' : 'middle'),
                row.isMine ? 'cursor-pointer hover:bg-primary-text/15' : '',
              ]"
              :title="
                cell.absences.length === 0
                  ? emptyCellTitle(row.isMine, cell.info.holidayTitle)
                  : undefined
              "
              :data-iso="cell.day.iso"
              @click="handleCellClick(row.person, cell)"
            >
              <div
                v-if="cell.absences.length > 0"
                class="w-full h-full flex items-center justify-center text-[10px] font-bold"
                :class="cell.painted ? 'bg-accent-solid text-on-accent' : ''"
                :title="cellTitle(row.person, cell.absences)"
              >
                {{
                  cell.isLabelCell
                    ? cell.wholeDay
                      ? absenceLabel(cell.wholeDay)
                      : timeBasedCellLabel(cell.timeBased)
                    : ""
                }}
              </div>
            </td>
          </tr>

          <!-- Padding rows so a small team's grid still fills the container instead of
        leaving bare space below the last person, keeping the day columns' borders and
        shading running the full height. -->
          <tr v-for="filler in fillerRows" :key="`filler-${filler.rowIndex}`" aria-hidden="true">
            <th
              scope="row"
              class="sticky left-0 z-10 border-b border-border shadow-[2px_0_0_0_var(--color-border)] px-2 py-1.5 align-middle"
            />
            <td
              v-for="(day, dayIndex) in days"
              :key="day.iso"
              class="h-9 p-0 align-middle border-b border-border"
              :class="[
                dayRenderInfo[dayIndex]!.bodyClass,
                todayColumnClass(day.isToday, filler.isLast ? 'bottom' : 'middle'),
              ]"
            />
          </tr>
        </tbody>
      </table>
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
import { parse, toDisplayTime, toIso } from "@/lib/dates";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import { usePersonStore } from "@/stores/person";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

const props = defineProps<{
  /** Filtered but not sorted - see `order` below for display sequence. */
  people: PersonDto[];
  /** Person ids in the order rows should render. Kept separate from `people` so that a
   * sort-mode change (reordering only) doesn't invalidate the per-person cell computation
   * below, which only cares about *which* people are visible, not what order they're in. */
  order: number[];
  absences: AbsenceDto[];
  holidays: BankHolidayDto[];
  year: number;
  loading: boolean;
}>();

const emit = defineEmits<{
  "add-absence": [date: string];
  "edit-absence": [absences: AbsenceDto[], date: string];
  "edit-holiday": [holiday: BankHolidayDto];
  "add-holiday": [date: string];
  reorder: [personId: number, targetPersonId: number];
}>();

const configStore = useConfigStore();
const personStore = usePersonStore();
const { regionLabels: peopleRegionLabels } = storeToRefs(usePeopleStore());
const containerRef = ref<HTMLDivElement | null>(null);
const theadRef = ref<HTMLTableSectionElement | null>(null);

function isMyRow(person: PersonDto): boolean {
  return person.id === personStore.person?.id;
}

// One handler for the whole cell, not a separate one on the absence's own label div: a
// day stacking several time-based absences needs one unambiguous click target rather than
// "click the one letter currently shown", so the cell is clickable for the viewer's own
// row regardless of what it holds. Non-owned rows stay non-clickable (tooltip only).
function handleCellClick(
  person: PersonDto,
  cell: { day: { iso: string }; absences: AbsenceDto[] },
) {
  if (!isMyRow(person)) return;
  if (cell.absences.length === 0) emit("add-absence", cell.day.iso);
  else emit("edit-absence", cell.absences, cell.day.iso);
}

// Native HTML5 drag-and-drop rather than a library: reordering a handful of rows
// doesn't need anything fancier, and it keeps this already-heavy component lean.
let draggedPersonId: number | null = null;

function handleDragStart(personId: number) {
  draggedPersonId = personId;
}

function handleDrop(targetPersonId: number) {
  if (draggedPersonId !== null && draggedPersonId !== targetPersonId) {
    emit("reorder", draggedPersonId, targetPersonId);
  }
  draggedPersonId = null;
}

// This listener sits on the container, not on each cell: a full year is thousands of
// cells, and one listener here covers any team size or date range. The highlight itself
// is applied imperatively, not through a reactive ref, so hovering only touches the DOM
// nodes for the column and row you're entering or leaving. With a year's worth of cells,
// routing this through Vue's reactivity would force a re-render of every cell on every
// crossing.
//
// Column and row are marked with plain boolean attributes rather than each writing its
// own inline box-shadow: the hovered cell sits in both at once, and two independent
// inline styles can only ever show the one written last. The two-attribute selector
// below in <style> gives that cell its own (stronger) look via CSS specificity instead.
let hoveredIso: string | null = null;
let hoveredPersonId: string | null = null;

function handleGridHover(event: MouseEvent) {
  const cell = (event.target as HTMLElement).closest<HTMLElement>("[data-iso]");
  const iso = cell?.dataset.iso ?? null;
  const personId = cell?.closest<HTMLElement>("tr[data-person-id]")?.dataset.personId ?? null;

  if (iso !== hoveredIso) setHoverColumn(iso);
  if (personId !== hoveredPersonId) setHoverRow(personId);
}

function clearHoverColumn() {
  setHoverColumn(null);
  setHoverRow(null);
}

function setHoverColumn(iso: string | null) {
  const container = containerRef.value;
  if (container && hoveredIso) {
    for (const el of container.querySelectorAll<HTMLElement>(`[data-iso="${hoveredIso}"]`)) {
      el.removeAttribute("data-col-hover");
    }
  }
  if (container && iso) {
    for (const el of container.querySelectorAll<HTMLElement>(`[data-iso="${iso}"]`)) {
      el.setAttribute("data-col-hover", "");
    }
  }
  hoveredIso = iso;
}

function setHoverRow(personId: string | null) {
  const container = containerRef.value;
  if (container && hoveredPersonId) {
    const row = container.querySelector<HTMLTableRowElement>(
      `tr[data-person-id="${hoveredPersonId}"]`,
    );
    for (const cell of row?.children ?? []) cell.removeAttribute("data-row-hover");
  }
  if (container && personId) {
    const row = container.querySelector<HTMLTableRowElement>(`tr[data-person-id="${personId}"]`);
    for (const cell of row?.children ?? []) cell.setAttribute("data-row-hover", "");
  }
  hoveredPersonId = personId;
}

onMounted(() => {
  configStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load region catalog:", error);
  });
  scrollToToday();

  updateRowCapacity();
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

/** Scrolls the grid so today's column is centered in view. No-op if today isn't in the displayed year. */
async function scrollToToday() {
  await nextTick();
  containerRef.value?.querySelector<HTMLElement>("[data-today]")?.scrollIntoView({
    inline: "center",
    block: "nearest",
  });
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

// region/country code -> display name, e.g. "CH-ZH" -> "Zürich", "CH" -> "Switzerland".
// Comes from the people payload rather than config: it covers wherever the team is now,
// which is also the only place holidays are stored for.
const regionLabels = computed(() => {
  const map = new Map<string, string>();
  for (const [code, label] of Object.entries(peopleRegionLabels.value)) {
    map.set(code, label);
  }
  return map;
});

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
  const today = toIso(new Date());

  const result = [];
  for (const d = new Date(props.year, 0, 1); d <= end; d.setDate(d.getDate() + 1)) {
    const iso = toIso(d);
    result.push({
      iso,
      month: d.getMonth(),
      dayNum: d.getDate(),
      isWeekend: d.getDay() === 0 || d.getDay() === 6,
      isToday: iso === today,
      weekdayLabel: weekdayLabels[d.getDay()],
    });
  }
  return result;
});

const monthGroups = computed(() => {
  const fmt = new Intl.DateTimeFormat(navigator.language, { month: "long" });
  const groups: { month: number; label: string; span: number }[] = [];

  for (const day of days.value) {
    const current = groups[groups.length - 1];
    if (current?.month === day.month) {
      current.span++;
    } else {
      groups.push({
        month: day.month,
        label: fmt.format(new Date(props.year, day.month, 1)),
        span: 1,
      });
    }
  }

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

// personId -> iso date -> every absence touching that day. An array rather than a single
// absence: a whole-day absence and a time-based one are mutually exclusive server-side, so
// a day holds at most one whole-day absence, but any number of same-day time-based ones
// can stack (see AbsenceService.OverlapCandidates).
const absenceMap = computed(() => {
  const map = new Map<number, Map<string, AbsenceDto[]>>();
  for (const a of props.absences) {
    let byDate = map.get(a.personId);
    if (!byDate) {
      byDate = new Map();
      map.set(a.personId, byDate);
    }
    for (const d = parse(a.startDate); ; d.setDate(d.getDate() + 1)) {
      const iso = toIso(d);
      if (iso > a.endDate) break;
      const list = byDate.get(iso);
      if (list) list.push(a);
      else byDate.set(iso, [a]);
    }
  }
  return map;
});

// A weekend isn't a work day, so an absence stretch doesn't paint over it: the day keeps
// its own weekend background instead of the person's color. A bank holiday used to work
// the same way, but its background shouldn't be able to hide that someone is actually off
// that day - an absence still paints straight through a holiday, same as any workday.
function isPaintDay(day: { isWeekend: boolean }): boolean {
  return !day.isWeekend;
}

// The day-level part of every body cell's rendering: the weekend/holiday background, the
// month-boundary side border, and the holiday tooltip are the same for every row, so
// they're resolved once per day here instead of once per cell in the template. Threaded
// into each cell by cellsForPerson below, which also makes a holiday change reach the
// cells through row.cells - and so through the rows' v-memo - instead of bypassing it.
const dayRenderInfo = computed(() =>
  days.value.map((day) => ({
    bodyClass: `${dayHeaderClass(day)} ${columnSideBorderClass(day)}`,
    holidayTitle: holidayName(day.iso),
  })),
);

// Most cells hold no absence at all, so the empty case is a fast path that allocates
// nothing: one shared empty array instead of a fresh [] (and a fresh .filter() result)
// per empty cell, per person.
const NO_ABSENCES: AbsenceDto[] = [];

// This precomputes each visible person's per-day absences once. Otherwise the template
// would look it up up to four times per cell (v-if, opacity, title, label). Now it's
// resolved a single time per cell, and only recalculated when the visible people,
// absences, or days actually change - not when they're merely reordered. That split
// matters: rebuilding a year of cells for everyone is the expensive part of this
// component, and a sort-mode change (see HomeView's `order` prop) is otherwise nothing
// more than that - it doesn't add, remove, or change anyone's absences.
//
// wholeDay is the at-most-one whole-day absence a day can hold (mutual exclusivity is
// enforced server-side); timeBased is everything else. isLabelCell marks only the first
// day of each contiguous run of *painted* days for the same wholeDay absence, so a 5-day
// vacation shows its "V" once instead of once per day (a weekend or holiday in the middle
// breaks the run, same as before). A time-based absence never joins a multi-day run -
// each is inherently single-day, so a cell holding one always shows its own label on any
// painted day, independently of its neighbors, and none on an unpainted one, same as a
// wholeDay absence.
function cellsForPerson(person: PersonDto) {
  const byDate = absenceMap.value.get(person.id);
  const infos = dayRenderInfo.value;
  let previousPaintedAbsence: AbsenceDto | undefined;

  return days.value.map((day, dayIndex) => {
    // Non-null: dayRenderInfo maps over the same days array this does.
    const info = infos[dayIndex]!;
    const absences = byDate?.get(day.iso);

    if (absences === undefined) {
      previousPaintedAbsence = undefined;
      return {
        day,
        info,
        absences: NO_ABSENCES,
        wholeDay: undefined as AbsenceDto | undefined,
        timeBased: NO_ABSENCES,
        painted: false,
        isLabelCell: false,
      };
    }

    const wholeDay = absences.find((a) => !a.startTime);
    const timeBased = absences.filter((a) => a.startTime);

    // absences is never empty here, so "has content" is already established: a cell
    // without a whole-day absence holds only time-based ones and always labels itself
    // on a painted day.
    const painted = isPaintDay(day);
    const isLabelCell =
      wholeDay !== undefined ? painted && wholeDay !== previousPaintedAbsence : painted;
    previousPaintedAbsence = painted && wholeDay !== undefined ? wholeDay : undefined;

    return { day, info, absences, wholeDay, timeBased, painted, isLabelCell };
  });
}

// Cells are cached per person id and survive a change in who's visible. Filtering the team
// - by region, or by the home page's name search - only ever adds and removes people, and
// whoever stays has exactly the cells they had a moment ago.
//
// The array identity is the whole point. Handing a surviving row back the *same* cells
// array is what lets the v-memo on the row template compare equal and skip the row
// outright; rebuilding the Map from scratch handed every row a fresh array instead, so
// Vue re-diffed a year of <td>s per person to confirm nothing had changed. With the cache
// the only rows touched are the ones that actually came or went.
//
// Nothing about a person other than their id feeds cellsForPerson, so a changed name or
// region can't stale an entry. Cell *content* comes from absenceMap and dayRenderInfo, and
// those two are replaced wholesale when they change, so comparing their identities is
// enough to know the cache is still good. days needs no check of its own: dayRenderInfo is
// built by mapping it, so a new year gives that a new identity too.
//
// Entries for people currently filtered out are kept. The ceiling is the whole roster,
// which is what's already in memory on first paint anyway, and it's what makes backspacing
// through a query instant instead of recomputing everyone who reappears.
let cellCache = new Map<number, ReturnType<typeof cellsForPerson>>();
let cachedAbsenceMap: typeof absenceMap.value | null = null;
let cachedDayRenderInfo: typeof dayRenderInfo.value | null = null;

const cellsByPersonId = computed(() => {
  if (absenceMap.value !== cachedAbsenceMap || dayRenderInfo.value !== cachedDayRenderInfo) {
    cellCache = new Map();
    cachedAbsenceMap = absenceMap.value;
    cachedDayRenderInfo = dayRenderInfo.value;
  }

  const visible = new Map<number, ReturnType<typeof cellsForPerson>>();
  for (const person of props.people) {
    let cells = cellCache.get(person.id);
    if (cells === undefined) {
      cells = cellsForPerson(person);
      cellCache.set(person.id, cells);
    }
    visible.set(person.id, cells);
  }
  return visible;
});

const personById = computed(() => new Map(props.people.map((person) => [person.id, person])));

// The rows the template actually renders, in display order. Deliberately cheap: index
// math and two map lookups per row, not a year of cell computation, so a sort-mode
// change - which only touches `order` - stays snappy no matter how big the team or the
// year is.
//
// No zebra-stripe background here anymore (see the CSS at the bottom of this file):
// striping used to be computed from each row's index, which meant a plain reorder gave
// nearly every row a "new" background value and forced the template to re-render and
// re-diff all of that row's day cells just to confirm most of them hadn't actually
// changed. Row position is exactly what the browser's own :nth-child already tracks for
// free, so it moved there, which also leaves this object's fields depending only on the
// *person* a row belongs to - stable across a reorder, and what makes the v-memo on the
// row template below actually able to skip unchanged rows.
const rows = computed(() =>
  props.order.map((personId) => {
    const person = personById.value.get(personId)!;
    const cells = cellsByPersonId.value.get(personId)!;
    const isMine = isMyRow(person);

    return {
      person,
      cells,
      isMine,
      // Code shown on the row itself (compact enough for the widened name column); the
      // full label is only surfaced on hover, via the title below.
      regionCode: person.homeCity,
      regionLabel: person.homeCity
        ? (regionLabels.value.get(person.homeCity) ?? person.homeCity)
        : null,
    };
  }),
);

// How many rows fit in the visible container. Only the container's own height needs
// measuring - every row (real, filler, or skeleton) is a fixed h-9, so row height itself
// is a constant, not something to read off the DOM. Backs both the filler rows below
// (padding a small team's grid to the bottom of the container) and the loading skeleton's
// row count (filling the same space, since a team's size isn't known yet while it loads).
const ROW_HEIGHT_PX = 36; // h-9
const rowCapacity = ref(0);
let resizeObserver: ResizeObserver | null = null;
let measureScheduled = false;

function updateRowCapacity() {
  const container = containerRef.value;
  if (!container) return;
  const headerHeight = theadRef.value?.getBoundingClientRect().height ?? 0;
  rowCapacity.value = Math.max(
    0,
    Math.floor((container.clientHeight - headerHeight) / ROW_HEIGHT_PX),
  );
}

// Coalesces bursts of layout-affecting changes - a window drag-resize fires many
// ResizeObserver callbacks a second, and a big rows/loading change patches hundreds of
// cells at once - down to one measurement per animation frame. Deferred past the current
// task rather than measured inline (the previous approach, via nextTick): reading layout
// synchronously right after a big DOM patch forces the browser to lay the whole grid out
// immediately instead of on its own schedule, which is what made switching sort modes feel
// sluggish. requestAnimationFrame alone is enough to run after that patch lands - it
// can't fire until the microtask queue (where Vue's own DOM updates are flushed) drains.
function scheduleRowCapacityUpdate() {
  if (measureScheduled) return;
  measureScheduled = true;
  requestAnimationFrame(() => {
    measureScheduled = false;
    updateRowCapacity();
    // Piggybacks on the same measurement pass: a year change resizes the table (and so
    // scrollWidth) without necessarily moving scrollLeft, which the scroll listener alone
    // wouldn't catch.
    updateScrollButtons();
  });
}

watch([rows, () => props.loading], scheduleRowCapacityUpdate);

// At least one row even before the first measurement lands, so the skeleton never
// flashes empty.
const skeletonRowCount = computed(() => Math.max(1, rowCapacity.value));

const fillerRowCount = computed(() => Math.max(0, rowCapacity.value - rows.value.length));

// Precomputed once per filler row (not re-derived per cell) for the same reason as
// `todayColumnClass`'s "is this the closing row" check below: it only needs to happen
// once, not once per day cell in that row.
const fillerRows = computed(() => {
  const bodyRowCount = rows.value.length;
  return Array.from({ length: fillerRowCount.value }, (_, i) => ({
    rowIndex: bodyRowCount + i,
    isLast: i === fillerRowCount.value - 1,
  }));
});

// The one real row the today-column's outline has to close its bottom edge on, if there's
// no filler row to close it on instead. Deliberately downstream of `rows`/`fillerRowCount`
// rather than folded into `rows` itself - `rows` computing this would need
// `fillerRowCount.value`, which itself depends on `rows.value.length`, a circular
// dependency. Compared per-row in the template (row.person.id === lastRowPersonId) rather
// than passed down as a shared value, so only the (at most two) rows whose own answer
// actually flips lose their v-memo on a reorder, not every row at once.
const lastRowPersonId = computed(() => {
  if (fillerRowCount.value > 0) return null;
  return rows.value.at(-1)?.person.id ?? null;
});

// Weekends and holidays keep their own opaque tint regardless of row, since those already
// mark the column and shouldn't be muddied by the row's own background. Everywhere else
// this returns nothing (plainBackground's default): those cells stay transparent and let
// the <tr>'s own background - the zebra stripe or the "mine" highlight, see the CSS at the
// bottom of this file - show through instead of each cell carrying its own copy of it.
//
// The day header row is the one caller that still wants an explicit opaque background of
// its own (plainBackground passed as 'bg-background'): it's sticky-positioned and in
// <thead>, not a body row, so there's no <tr> stripe underneath it to show through.
function dayHeaderClass(day: { iso: string; isWeekend: boolean }, plainBackground = "") {
  if (holidayMap.value.has(day.iso)) return "bg-holiday";
  if (day.isWeekend) return "bg-surface";
  return plainBackground;
}

// Today's column is outlined by an overlay pseudo-element rather than by a border on the
// cell itself. A real border takes up space in the cell's box, which pushed the day number
// off-centre and made today's column wider than its neighbours. The overlay is absolutely
// positioned, so it costs no layout and still paints above a filled-in absence block
// instead of being hidden underneath it.
const TODAY_OUTLINE =
  "relative after:content-[''] after:absolute after:inset-0 after:pointer-events-none " +
  "after:border-l-2 after:border-r-2 after:border-today";

// The header row is always the top of the outline; only the last row has to close it off.
function todayColumnClass(isToday: boolean, edge: "top" | "middle" | "bottom"): string {
  if (!isToday) return "";
  if (edge === "top") return `${TODAY_OUTLINE} after:border-t-2`;
  if (edge === "bottom") return `${TODAY_OUTLINE} after:border-b-2`;
  return TODAY_OUTLINE;
}

// The current user's row gets the same outline treatment as today's column above: an
// overlaid pseudo-element rather than a real border, so it costs no layout and doesn't
// compete with the cell's own border-b/border-l. Top and bottom close off every cell in
// the row; left/right close off the two ends (the sticky name cell and the row's last day
// column), same as today's column closing off with its own top/bottom.
const MINE_ROW_OUTLINE =
  "after:content-[''] after:absolute after:inset-0 after:pointer-events-none " +
  "after:border-t-2 after:border-b-2 after:border-primary-text";

function mineRowClass(isMine: boolean, edge: "start" | "middle" | "end"): string {
  if (!isMine) return "";
  if (edge === "start") return `${MINE_ROW_OUTLINE} after:border-l-2`;
  if (edge === "end") return `${MINE_ROW_OUTLINE} after:border-r-2`;
  return MINE_ROW_OUTLINE;
}

// Every day gets a real left border, not just the bottom one: this is the column-axis
// equivalent of border-b, and using left rather than right avoids doubling up with the
// month-boundary border below (each divider has exactly one owner, the column to its
// right, same as border-b's "the row above owns it" rule). Month starts get a thicker,
// stronger-colored version of the same border so the grouping still stands out from the
// day-to-day grid lines.
//
// On the rare day the 1st *is* today, or any day is today, the border is left transparent
// and today's own outline (see TODAY_OUTLINE) reads as the divider instead - drawn as an
// overlay so it never nudges the day number off-centre the way a colored real border
// would. The transparent border still reserves the same width, so column widths stay
// uniform whether or not today happens to fall on a month boundary.
function columnSideBorderClass(day: { isToday: boolean; dayNum: number }): string {
  if (day.dayNum !== 1) {
    return day.isToday ? "border-l border-l-transparent" : "border-l border-border";
  }
  return day.isToday ? "border-l-2 border-l-transparent" : "border-l-2 border-l-border";
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
  return entries[0].source === "Manual" ? entries[0] : undefined;
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

// The letter the legend shows, with a trailing "?" for anything still awaiting review, so
// a pending absence reads as "color, but unconfirmed" without needing a second visual
// language on top of the one the calendar already has.
function absenceLabel(absence: AbsenceDto): string {
  const initial = ABSENCE_TYPE_INITIALS[absence.type];
  return absence.status === "Pending" ? `${initial}?` : initial;
}

// Same "?" reasoning as absenceLabel, but for the whole day's stack of time-based absences
// at once: pending if any one of them is. Letter comes from the first entry's own type -
// the stack is usually one type, and on the rare day it isn't (e.g. an Appointment and a
// time-based Other), the tooltip is what disambiguates the rest.
function timeBasedCellLabel(entries: AbsenceDto[]): string {
  const initial = ABSENCE_TYPE_INITIALS[entries[0].type];
  return entries.some((a) => a.status === "Pending") ? `${initial}?` : initial;
}

// Every absence on the cell, own or not - tooltips are visible for everyone, only editing
// is restricted (see handleCellClick's isMyRow guard). Appended one per line so several
// same-day time-based absences each get their own line, as requested.
function cellTitle(person: PersonDto, absences: AbsenceDto[]): string {
  const name = person.displayName ?? person.username;
  const lines = absences.map((a) => {
    const status = a.status === "Pending" ? " (pending approval)" : "";
    const timeRange = a.startTime
      ? ` ${toDisplayTime(a.startTime)}–${toDisplayTime(a.endTime)}`
      : "";
    const body = a.note ? `${a.type}${timeRange}: ${a.note}` : `${a.type}${timeRange}`;
    return `${name} — ${body}${status}`;
  });
  // Once at the end rather than repeated per line: every time here is in the same zone,
  // same as the add/edit form's own (Europe/Zurich) labels.
  if (absences.some((a) => a.startTime) && configStore.config)
    lines.push(`(times in ${configStore.config.homeTimeZone})`);
  if (isMyRow(person)) lines.push("(click to edit)");
  return lines.join("\n");
}

function emptyCellTitle(isMine: boolean, holidayTitle: string): string {
  if (!isMine) return holidayTitle;
  return holidayTitle ? `${holidayTitle} (click to add absence)` : "Click to add absence";
}

const tableMinWidth = computed(() => `${160 + days.value.length * 28}px`);
</script>

<style scoped>
/* [data-col-hover] and [data-row-hover] are toggled imperatively in handleGridHover
   rather than reactively, see the comment there. The cell where the hovered row and
   column intersect carries both attributes, and the two-attribute selector below wins
   over either single-attribute rule on specificity alone, so that one cell reads as more
   highlighted without any extra bookkeeping in JS. */
[data-col-hover],
[data-row-hover] {
  box-shadow: inset 0 0 0 9999px color-mix(in srgb, var(--color-primary-text) 10%, transparent);
}

[data-col-hover][data-row-hover] {
  box-shadow: inset 0 0 0 9999px color-mix(in srgb, var(--color-primary-text) 20%, transparent);
}

/* Zebra striping and the "mine" row highlight live here instead of a per-row JS class:
   row position is exactly what :nth-child already tracks for free from the live DOM, so
   computing it in JS on every render (as this used to) made a reorder look like every
   row's content had changed - see the comment on `rows` in the script above. tbody covers
   the real table body, the filler rows padding it, and the loading skeleton's body alike,
   all plain <tr> siblings within it, so the stripe continues seamlessly across all three
   without special-casing any of them. Split into three rules with :not() rather than
   relying on source order or specificity to let .bg-row-mine win over the stripe, so the
   two never have to compete for the same row. */
tbody tr {
  background-color: var(--color-background);
}

tbody tr:nth-child(even):not(.bg-row-mine) {
  background-color: var(--color-row-alt);
}

tbody tr.bg-row-mine {
  background-color: var(--color-row-mine);
}

/* The name column's <th> repeats the same three rules for itself rather than relying on
   the <tr> background painting "through" it: that layering only holds for a normal table
   cell. This one is position: sticky, which visually detaches it from its logical
   position during scroll, so it needs its own explicit background to stay opaque - the
   same requirement the JS version of this class used to meet directly, see the comment on
   `rows` above. */
tbody tr > th {
  background-color: var(--color-background);
}

tbody tr:nth-child(even):not(.bg-row-mine) > th {
  background-color: var(--color-row-alt);
}

tbody tr.bg-row-mine > th {
  background-color: var(--color-row-mine);
}
</style>
