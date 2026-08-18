<template>
  <div
    ref="containerRef"
    class="overflow-auto flex-1 min-h-0 border border-border rounded-12 shadow-card"
    @mouseover="handleGridHover"
    @mouseleave="clearHoverColumn"
  >
    <table
      class="w-full table-fixed border-separate border-spacing-0"
      :style="{ minWidth: tableMinWidth }"
    >
      <colgroup>
        <col style="width: 120px" />
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
              dayHeaderClass(day),
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
            <div class="font-semibold" :class="day.isToday ? 'text-today' : 'text-ink'">{{ day.dayNum }}</div>
            <div class="text-[10px]" :class="day.isToday ? 'text-today font-semibold' : 'text-secondary'">
              {{ day.weekdayLabel }}
            </div>
          </th>
        </tr>
      </thead>

      <tbody v-if="loading">
        <tr v-for="n in 5" :key="`skeleton-${n}`">
          <th
            scope="row"
            class="sticky left-0 bg-background z-10 border-b border-border shadow-[2px_0_0_0_var(--color-border)] px-2 py-1.5 align-middle"
          >
            <AppSkeleton class="h-4 w-20" />
          </th>
          <td :colspan="days.length" class="border-b border-border h-9 p-1.5 align-middle">
            <AppSkeleton class="h-full w-full" />
          </td>
        </tr>
      </tbody>

      <tbody v-else>
        <tr v-for="(row, rowIndex) in rows" :key="row.person.id" :data-person-id="row.person.id">
          <th
            scope="row"
            class="sticky left-0 z-10 border-b border-border shadow-[2px_0_0_0_var(--color-border)] px-2 py-1.5 text-sm font-normal text-left align-middle cursor-grab active:cursor-grabbing"
            :class="[
              row.background,
              row.isMine ? 'border-l-[3px] border-l-primary-text' : 'border-l-[3px] border-l-transparent',
            ]"
            draggable="true"
            title="Drag to reorder"
            @dragstart="handleDragStart(row.person.id)"
            @dragover.prevent
            @drop="handleDrop(row.person.id)"
          >
            <div class="flex items-center gap-2">
              <span class="text-secondary/40 select-none text-xs leading-none">⠿</span>
              <span class="truncate" :class="row.isMine ? 'text-primary-text font-semibold' : 'text-ink'">{{
                row.person.displayName ?? row.person.username
              }}</span>
              <span v-if="row.isMine" class="text-[10px] text-primary-text font-semibold shrink-0">You</span>
            </div>
          </th>

          <td
            v-for="cell in row.cells"
            :key="`${row.person.id}-${cell.day.iso}`"
            class="h-9 p-0 align-middle border-b border-border"
            :class="[
              dayHeaderClass(cell.day, row.background),
              columnSideBorderClass(cell.day),
              todayColumnClass(
                cell.day.isToday,
                rowIndex === rows.length - 1 && fillerRowCount === 0 ? 'bottom' : 'middle',
              ),
              row.isMine ? 'cursor-pointer hover:bg-primary-text/15' : '',
            ]"
            :title="cell.absences.length === 0 ? emptyCellTitle(row.person, cell.day) : undefined"
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
            :class="filler.background"
          />
          <td
            v-for="day in days"
            :key="day.iso"
            class="h-9 p-0 align-middle border-b border-border"
            :class="[
              dayHeaderClass(day, filler.background),
              columnSideBorderClass(day),
              todayColumnClass(day.isToday, filler.isLast ? 'bottom' : 'middle'),
            ]"
          />
        </tr>
      </tbody>
    </table>
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
  people: PersonDto[];
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
function handleCellClick(person: PersonDto, cell: { day: { iso: string }; absences: AbsenceDto[] }) {
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
    const row = container.querySelector<HTMLTableRowElement>(`tr[data-person-id="${hoveredPersonId}"]`);
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

  updateFillerRowCount();
  if (containerRef.value) {
    resizeObserver = new ResizeObserver(scheduleFillerRowUpdate);
    resizeObserver.observe(containerRef.value);
  }
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
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
  const start = new Date(props.year, 0, 1);
  const end = new Date(props.year, 11, 31);
  const today = toIso(new Date());

  const result = [];
  for (let d = start; d <= end; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) {
    const iso = toIso(d);
    result.push({
      iso,
      month: d.getMonth(),
      dayNum: d.getDate(),
      isWeekend: d.getDay() === 0 || d.getDay() === 6,
      isToday: iso === today,
      weekdayLabel: weekdayFmt.format(d),
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
    for (let d = parse(a.startDate); toIso(d) <= a.endDate; d.setDate(d.getDate() + 1)) {
      const iso = toIso(d);
      const list = byDate.get(iso);
      if (list) list.push(a);
      else byDate.set(iso, [a]);
    }
  }
  return map;
});

// Weekends and bank holidays aren't work days, so an absence stretch doesn't paint over
// them: the day keeps its own weekend/holiday background instead of the person's color.
function isPaintDay(day: { iso: string; isWeekend: boolean }): boolean {
  return !day.isWeekend && !holidayMap.value.has(day.iso);
}

// This precomputes each row's per-day absences once. Otherwise the template would look
// it up up to four times per cell (v-if, opacity, title, label). Now it's resolved a
// single time per cell, and only recalculated when people, absences, or days change.
//
// wholeDay is the at-most-one whole-day absence a day can hold (mutual exclusivity is
// enforced server-side); timeBased is everything else. isLabelCell marks only the first
// day of each contiguous run of *painted* days for the same wholeDay absence, so a 5-day
// vacation shows its "V" once instead of once per day (a weekend or holiday in the middle
// breaks the run, same as before). A time-based absence never joins a multi-day run -
// each is inherently single-day, so a cell holding one always shows its own label on any
// painted day, independently of its neighbors, and none on an unpainted one, same as a
// wholeDay absence.
const rows = computed(() =>
  props.people.map((person, rowIndex) => {
    const byDate = absenceMap.value.get(person.id);
    let previousPaintedAbsence: AbsenceDto | undefined;

    const cells = days.value.map((day) => {
      const absences = byDate?.get(day.iso) ?? [];
      const wholeDay = absences.find((a) => !a.startTime);
      const timeBased = absences.filter((a) => a.startTime);

      const hasContent = wholeDay !== undefined || timeBased.length > 0;
      const painted = hasContent && isPaintDay(day);
      const isLabelCell =
        wholeDay !== undefined ? painted && wholeDay !== previousPaintedAbsence : painted && timeBased.length > 0;
      previousPaintedAbsence = painted && wholeDay !== undefined ? wholeDay : undefined;

      return { day, absences, wholeDay, timeBased, painted, isLabelCell };
    });

    // Resolved once per row rather than once per cell, same reasoning as `cells` above.
    // bg-row-mine, not a translucent bg-primary-text/N: this backs the sticky name cell
    // too (see --color-row-mine), which needs to stay fully opaque to occlude day columns
    // scrolling underneath it rather than letting them show through.
    const isMine = isMyRow(person);
    return { person, cells, isMine, background: isMine ? "bg-row-mine" : rowBackgroundClass(rowIndex) };
  }),
);

// Keeps the grid's body filled to the bottom of the container even when a team has
// too few people to reach it, rather than leaving bare space below the last row.
// Recomputed from actual measured pixels (not a fixed guess) since the container's
// height now comes from flex layout and varies with viewport and header content.
const FALLBACK_ROW_HEIGHT_PX = 36; // h-9, used only before any row has rendered to measure
const fillerRowCount = ref(0);
let resizeObserver: ResizeObserver | null = null;
let measureScheduled = false;

function updateFillerRowCount() {
  const container = containerRef.value;
  if (!container) return;
  const headerHeight = theadRef.value?.getBoundingClientRect().height ?? 0;
  const rowHeight =
    container.querySelector("tbody tr")?.getBoundingClientRect().height || FALLBACK_ROW_HEIGHT_PX;
  const availableRows = Math.floor((container.clientHeight - headerHeight) / rowHeight);
  fillerRowCount.value = Math.max(0, availableRows - rows.value.length);
}

// Coalesces bursts of ResizeObserver callbacks (e.g. a window drag-resize fires many
// times per second) down to once per animation frame, so the measurement work below
// doesn't run more often than the browser can actually paint.
function scheduleFillerRowUpdate() {
  if (measureScheduled) return;
  measureScheduled = true;
  requestAnimationFrame(() => {
    measureScheduled = false;
    updateFillerRowCount();
  });
}

watch([rows, () => props.loading], () => nextTick(updateFillerRowCount));

// Precomputed once per filler row (not re-derived per cell) for the same reason as
// `rows.background` above: index math and the "is this the closing row" check only
// need to happen once, not once per day cell in that row.
const fillerRows = computed(() => {
  const bodyRowCount = rows.value.length;
  return Array.from({ length: fillerRowCount.value }, (_, i) => ({
    rowIndex: bodyRowCount + i,
    background: rowBackgroundClass(bodyRowCount + i),
    isLast: i === fillerRowCount.value - 1,
  }));
});

// Always resolves to exactly one background, including the "none of the above" case
// (plain bg-background), so the sticky header fully occludes rows scrolling underneath it.
// Today is marked separately with a ring, not a background, so it never has to compete
// with a holiday's own tint.
//
// plainBackground is left at its default for the day header row (the page background).
// Body rows pass their precomputed zebra-stripe background instead, to make rows
// easier to tell apart. Weekends and holidays keep their own tint regardless of row,
// since those already mark the column and shouldn't be muddied by the row stripe.
function dayHeaderClass(
  day: { iso: string; isWeekend: boolean },
  plainBackground = "bg-background",
) {
  if (holidayMap.value.has(day.iso)) return "bg-holiday";
  if (day.isWeekend) return "bg-surface";
  return plainBackground;
}

// Same stripe used on the sticky name column, so the alternating background reads as
// one continuous row rather than stopping at the day columns.
function rowBackgroundClass(rowIndex: number): string {
  return rowIndex % 2 === 1 ? "bg-row-alt" : "bg-background";
}

// Today's column is outlined (and tinted) by an overlay pseudo-element rather than by a
// border/background on the cell itself. A real border takes up space in the cell's box,
// which pushed the day number off-centre and made today's column wider than its
// neighbours. The overlay is absolutely positioned, so it costs no layout and still
// paints above a filled-in absence block instead of being hidden underneath it.
const TODAY_OUTLINE =
  "relative after:content-[''] after:absolute after:inset-0 after:pointer-events-none "
  + "after:border-l-2 after:border-r-2 after:border-today after:bg-today/10";

// The header row is always the top of the outline; only the last row has to close it off.
function todayColumnClass(isToday: boolean, edge: "top" | "middle" | "bottom"): string {
  if (!isToday) return "";
  if (edge === "top") return `${TODAY_OUTLINE} after:border-t-2`;
  if (edge === "bottom") return `${TODAY_OUTLINE} after:border-b-2`;
  return TODAY_OUTLINE;
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
    const timeRange = a.startTime ? ` ${toDisplayTime(a.startTime)}–${toDisplayTime(a.endTime)}` : "";
    const body = a.note ? `${a.type}${timeRange}: ${a.note}` : `${a.type}${timeRange}`;
    return `${name} — ${body}${status}`;
  });
  if (isMyRow(person)) lines.push("(click to edit)");
  return lines.join("\n");
}

function emptyCellTitle(person: PersonDto, day: { iso: string }): string {
  const holiday = holidayName(day.iso);
  if (!isMyRow(person)) return holiday;
  return holiday ? `${holiday} (click to add absence)` : "Click to add absence";
}

const tableMinWidth = computed(() => `${120 + days.value.length * 28}px`);
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
</style>
