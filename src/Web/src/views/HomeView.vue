<template>
  <div
    class="w-[min(100vw,1920px)] ml-[calc(50%-min(50vw,960px))] h-full flex flex-col gap-4 min-h-0"
  >
    <header class="flex items-center gap-3 flex-wrap">
      <!-- Everything that changes what the grid shows, or in what order. Only the search
           box is always out: the rest sit behind an icon each, so the header costs the
           width of what you're actually using. Each toggle carries a dot when its control
           is away from its default, since a filter you can't see is otherwise a filter you
           can't tell is on. -->
      <div class="flex-1 flex items-center gap-2 flex-wrap">
        <div class="relative">
          <!-- The icon and the shortcut hint are absolutely positioned over the input's own
               padding, so neither costs any layout and the input stays one element. -->
          <input
            id="personSearch"
            ref="searchInput"
            v-model="searchQuery"
            type="search"
            aria-label="Search people"
            placeholder="Search by name…"
            class="h-9 w-56 border border-border-strong rounded-8 pl-9 pr-12 text-sm bg-background hover:border-secondary"
            @keydown.escape="searchQuery = ''"
          />
          <svg
            class="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-secondary pointer-events-none"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <circle cx="11" cy="11" r="7" />
            <line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>
          <!-- The shortcut sits here rather than inside the placeholder, which had to spell
               it out in parentheses and lost it the moment you typed a character. Hidden
               once there's text, where the browser puts type=search's own clear button. -->
          <kbd
            v-if="!searchQuery"
            class="absolute right-2 top-1/2 -translate-y-1/2 rounded-4 border border-border px-1.5 py-0.5 font-body text-xs text-secondary pointer-events-none"
          >
            {{ searchShortcut }}
          </kbd>
        </div>

        <IconPopover ref="sortPopover" title="Sort" :active="sortMode !== 'mine'" :min-width="230">
          <template #icon>
            <svg
              class="w-5 h-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <path d="M8 20V4" />
              <path d="M4 8l4-4 4 4" />
              <path d="M16 4v16" />
              <path d="M20 16l-4 4-4-4" />
            </svg>
          </template>

          <button
            v-for="option in SORT_OPTIONS"
            :key="option.value"
            type="button"
            class="w-full flex items-center gap-2 px-3 py-1.5 text-sm text-left text-ink hover:bg-surface"
            :class="sortMode === option.value ? 'font-semibold' : undefined"
            @click="selectSort(option.value)"
          >
            <span class="w-3 shrink-0 text-accent">{{ sortMode === option.value ? "✓" : "" }}</span>
            {{ option.label }}
          </button>
        </IconPopover>

        <IconPopover
          ref="regionPopover"
          title="Filter"
          :active="regionFilter.length > 0 || year !== currentYear"
          :min-width="260"
        >
          <template #icon>
            <svg
              class="w-5 h-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <path d="M22 3H2l8 9.46V19l4 2v-8.54L22 3z" />
            </svg>
          </template>

          <div class="flex items-center justify-between gap-2 px-3 py-1.5 border-b border-border">
            <span class="text-xs font-semibold text-secondary uppercase tracking-wide">Year</span>
            <YearPicker v-model="year">
              <span class="text-sm font-semibold px-1 tabular-nums">{{ year }}</span>
            </YearPicker>
          </div>

          <p class="px-3 pt-2 pb-1 text-xs font-semibold text-secondary uppercase tracking-wide">
            Region
          </p>
          <!-- The list itself, not RegionFilterSelect: its own dropdown inside this panel
               would be a floating panel opening out of a floating panel. -->
          <RegionCheckboxList v-model="regionFilter" :groups="regionGroups" />
        </IconPopover>
      </div>

      <button
        type="button"
        class="shrink-0 h-9 flex items-center justify-center bg-primary text-on-primary border border-transparent rounded-8 px-3 text-sm font-semibold transition-all hover:bg-primary-hover hover:shadow-raised"
        @click="showWhosOffModal = true"
      >
        Who's off today
      </button>

      <div class="flex-1 flex items-center justify-end gap-2">
        <!-- Wrapper carries the tooltip: a disabled button fires no pointer events, so the
             explanation of why it's disabled would never be readable on the button itself. -->
        <span
          class="inline-flex"
          :data-tooltip="!personStore.person ? 'Set up your profile first!' : undefined"
        >
          <button
            type="button"
            class="h-9 w-42 flex items-center justify-center border border-accent text-accent rounded-8 px-3 text-sm font-semibold hover:bg-accent/10 transition-colors disabled:opacity-40 disabled:hover:bg-background"
            :disabled="!personStore.person"
            @click="openAddAbsence()"
          >
            + Add absence
          </button>
        </span>
        <!-- Wrapper carries the tooltip: a disabled button fires no pointer events, so the
             explanation of why it's disabled would never be readable on the button itself. -->
        <span
          class="inline-flex"
          :data-tooltip="!personStore.person ? 'Set up your profile first!' : undefined"
        >
          <button
            type="button"
            class="h-9 w-42 flex items-center justify-center border border-accent text-accent rounded-8 px-3 text-sm font-semibold hover:bg-accent/10 transition-colors disabled:opacity-40 disabled:hover:bg-background"
            :disabled="!personStore.person"
            @click="openAddBankHoliday()"
          >
            + Add bank holiday
          </button>
        </span>
      </div>
    </header>

    <p v-if="emptyMessage" class="text-sm text-secondary">{{ emptyMessage }}</p>

    <CalendarGrid
      ref="calendarGridRef"
      :people="filteredPeople"
      :order="visibleOrder"
      :absences="absences"
      :holidays="holidays"
      :year="year"
      :loading="loading"
      :reorderable="searchScores === null"
      @add-absence="openAddAbsence"
      @edit-absence="openEditAbsence"
      @edit-holiday="openEditHoliday"
      @add-holiday="openAddBankHoliday"
      @reorder="handleReorder"
    />

    <div class="flex items-center justify-between flex-wrap gap-3">
      <div class="flex items-center gap-4 text-xs text-secondary flex-wrap">
        <div class="flex items-center gap-1.5">
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-background ring-2 ring-inset ring-today"
          />
          <span>Today</span>
        </div>
        <div class="flex items-center gap-1.5">
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-holiday border border-border-strong"
          />
          <span>Bank holiday</span>
        </div>
        <div
          v-for="(initial, label) in ABSENCE_TYPE_INITIALS"
          :key="label"
          class="flex items-center gap-1.5"
        >
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-accent-solid text-on-accent text-xs font-bold"
            >{{ initial }}</span
          >
          <span>{{ label }}</span>
        </div>
      </div>

      <button
        type="button"
        class="text-sm font-semibold text-accent border border-accent rounded-8 px-3.5 py-1.5 hover:bg-accent/10 transition-colors"
        @click="goToToday"
      >
        Go to today
      </button>
    </div>

    <AddAbsenceModal
      v-if="showAbsenceModal"
      :initial-date="prefillDate"
      :day-absences="dayAbsences"
      @close="showAbsenceModal = false"
      @saved="handleAbsenceSaved"
      @deleted="handleAbsenceDeleted"
    />
    <AddBankHolidayModal
      v-if="showHolidayModal"
      :holiday="editingHoliday"
      :initial-date="prefillHolidayDate"
      @close="showHolidayModal = false"
      @saved="handleHolidaySaved"
      @deleted="handleHolidayDeleted"
    />
    <WhosOffTodayModal v-if="showWhosOffModal" @close="showWhosOffModal = false" />
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, nextTick, onMounted, onUnmounted, ref, shallowRef, watch } from "vue";

import { getAbsences } from "@/api/absences";
import { getCatalogHolidays } from "@/api/bankHolidays";
import AddAbsenceModal from "@/components/AddAbsenceModal.vue";
import AddBankHolidayModal from "@/components/AddBankHolidayModal.vue";
import CalendarGrid from "@/components/CalendarGrid.vue";
import IconPopover from "@/components/IconPopover.vue";
import RegionCheckboxList from "@/components/RegionCheckboxList.vue";
import WhosOffTodayModal from "@/components/WhosOffTodayModal.vue";
import YearPicker from "@/components/YearPicker.vue";
import { useStaleGuard } from "@/composables/useStaleGuard";
import { clearCacheGroup, readCache, writeCache } from "@/lib/browserCache";
import { toIso, today } from "@/lib/dates";
import { fuzzyScore, normalizeForSearch } from "@/lib/fuzzy";
import { reconcileList } from "@/lib/reconcile";
import { groupRegions } from "@/lib/regionGroups";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import { usePersonStore } from "@/stores/person";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

type SortMode = "name" | "mine" | "absentToday" | "region" | "custom";

// A list rather than <option>s now that the sort control lives in a popover. Order here is
// the order the panel shows them in.
const SORT_OPTIONS: { value: SortMode; label: string }[] = [
  { value: "mine", label: "Me first, then name" },
  { value: "name", label: "Name" },
  { value: "absentToday", label: "Absent today, then name" },
  { value: "region", label: "Region, then name" },
  { value: "custom", label: "Custom order" },
];

const { people, regionLabels } = storeToRefs(usePeopleStore());
const personStore = usePersonStore();
const configStore = useConfigStore();

// The filter offers exactly where the team is, which the API sends alongside the people it
// is filtering. Deriving it rather than reading a configured list is what lets someone
// joining a new region show up here without a redeploy.
const regionGroups = computed(() => groupRegions(regionLabels.value));

// Fixed at load, and only ever compared against: it's what "the year isn't the default
// one" means for the filter toggle's dot, not a value anything renders.
const currentYear = new Date().getFullYear();
const year = ref(currentYear);
// shallowRef: both are only ever replaced wholesale by loadYear, never mutated in place,
// and a deep ref would wrap a whole year of absence objects in reactive proxies - memory
// spent on tracking nothing, plus proxy overhead in every computed that iterates them.
const absences = shallowRef<AbsenceDto[]>([]);
const holidays = shallowRef<BankHolidayDto[]>([]);
const loading = ref(false);
const calendarGridRef = ref<InstanceType<typeof CalendarGrid> | null>(null);
const showAbsenceModal = ref(false);
const showHolidayModal = ref(false);
const showWhosOffModal = ref(false);
const prefillDate = ref<string | null>(null);
const prefillHolidayDate = ref<string | null>(null);
const dayAbsences = ref<AbsenceDto[]>([]);
const editingHoliday = ref<BankHolidayDto | null>(null);

const sortMode = ref<SortMode>(readCache<SortMode>("teamSortMode") ?? "mine");
// Array now, not a single code: coerced defensively since an older cached value here
// (from before multi-select) was a plain string, not an array.
const cachedRegionFilter = readCache<string[]>("teamRegionFilter");
const regionFilter = ref<string[]>(Array.isArray(cachedRegionFilter) ? cachedRegionFilter : []);
const customOrder = ref<number[]>(readCache<number[]>("teamCustomOrder") ?? []);
// Not cached across reloads, unlike the sort mode and region filter: a half-typed name is
// about what you're doing right now, not how you like the grid set up. Read directly by
// the grid's filtering: CalendarGrid only renders the rows in view, so a keystroke's
// update is a handful of rows and lands within the same frame as the typed character.
const searchQuery = ref("");

watch(sortMode, (mode) => writeCache("teamSortMode", mode));
watch(regionFilter, (regions) => writeCache("teamRegionFilter", regions));

const yearRange = computed(() => {
  const from = new Date(year.value, 0, 1);
  const to = new Date(year.value, 11, 31);
  return { from: toIso(from), to: toIso(to) };
});

function openAddAbsence(date: string | null = null) {
  dayAbsences.value = [];
  prefillDate.value = date;
  showAbsenceModal.value = true;
}

function openEditAbsence(absences: AbsenceDto[], date: string) {
  dayAbsences.value = absences;
  // Kept, unlike before: it's what "add another appointment for this day" inside the
  // modal's list view falls back to when it opens a fresh add form.
  prefillDate.value = date;
  showAbsenceModal.value = true;
}

function openAddBankHoliday(date: string | null = null) {
  editingHoliday.value = null;
  prefillHolidayDate.value = date;
  showHolidayModal.value = true;
}

function openEditHoliday(holiday: BankHolidayDto) {
  editingHoliday.value = holiday;
  prefillHolidayDate.value = null;
  showHolidayModal.value = true;
}

function personLabel(person: PersonDto): string {
  return (person.displayName ?? person.username).toLowerCase();
}

// A selected code matches its own region exactly, or any subdivision under it. So if
// you pick a country like "CH", you also get people set to "CH-ZH", "CH-BE", etc. No
// codes selected means no filtering at all, and any one selected code matching is enough.
function matchesRegionFilter(homeCity: string | null): boolean {
  if (regionFilter.value.length === 0) return true;
  if (!homeCity) return false;
  return regionFilter.value.some((code) => homeCity === code || homeCity.startsWith(`${code}-`));
}

// Falls back to the bare code so someone whose region isn't in the team's current
// catalog (yet, or anymore) still sorts somewhere sensible instead of crashing the
// comparator.
function regionLabel(code: string): string {
  return regionLabels.value[code] ?? code;
}

// Every sort mode falls back to name, so they only differ in the primary rank they give
// each person. Whatever that rank needs is indexed once here rather than recomputed
// inside the comparator, which would otherwise re-scan every absence on each comparison.
function primaryRank(): (person: PersonDto) => number {
  if (sortMode.value === "custom" && customOrder.value.length > 0) {
    const order = new Map(customOrder.value.map((id, index) => [id, index]));
    return (p) => order.get(p.id) ?? Number.MAX_SAFE_INTEGER;
  }

  if (sortMode.value === "mine") {
    const myId = personStore.person?.id;
    return (p) => (p.id === myId ? 0 : 1);
  }

  if (sortMode.value === "absentToday") {
    const iso = today();
    const absent = new Set(
      absences.value.filter((a) => a.startDate <= iso && a.endDate >= iso).map((a) => a.personId),
    );
    return (p) => (absent.has(p.id) ? 0 : 1);
  }

  if (sortMode.value === "region") {
    // Ranked by each region's own label rather than its code, so the order reads
    // alphabetically the way it's displayed, not by ISO code. Nobody's homeCity sorts
    // last, after every region that has one.
    const codes = [...new Set(people.value.map((p) => p.homeCity))].sort((a, b) => {
      if (!a) return 1;
      if (!b) return -1;
      return regionLabel(a).localeCompare(regionLabel(b));
    });
    const order = new Map(codes.map((code, index) => [code, index]));
    return (p) => order.get(p.homeCity) ?? Number.MAX_SAFE_INTEGER;
  }

  return () => 0; // by name alone
}

// Normalized once per roster rather than once per keystroke: the names don't change while
// someone is typing, and stripping diacritics off every name on every character would be
// the most expensive thing the search does.
const searchKeys = computed(
  () => new Map(people.value.map((p) => [p.id, normalizeForSearch(p.displayName ?? p.username)])),
);

// null means "no query": filtering and ranking both skip out entirely rather than scoring
// everyone against an empty string.
const searchScores = computed<Map<number, number> | null>(() => {
  const needle = normalizeForSearch(searchQuery.value.trim());
  if (!needle) return null;

  const scores = new Map<number, number>();
  for (const person of people.value) {
    const score = fuzzyScore(searchKeys.value.get(person.id)!, needle);
    if (score !== null) scores.set(person.id, score);
  }
  return scores;
});

// Filtering only, no sorting: CalendarGrid keys its per-person content off this list, so
// switching sort mode - which only ever changes display order, never membership - doesn't
// invalidate it. See visibleOrder below for the cheap part that does change with sort mode.
const filteredPeople = computed(() => {
  const scores = searchScores.value;
  return people.value.filter(
    (p) => matchesRegionFilter(p.homeCity) && (scores === null || scores.has(p.id)),
  );
});

// Why the grid is empty, or null when it isn't (or when there's no team loaded yet to be
// empty of). Naming the search outright is only honest when the search is what emptied it:
// a query can match someone the region filter then removes, and blaming "nobody matches
// john" when John is right there but filtered out to another region reads as a bug.
const emptyMessage = computed<string | null>(() => {
  if (people.value.length === 0 || filteredPeople.value.length > 0) return null;
  const scores = searchScores.value;
  if (scores?.size === 0) return `Nobody matches “${searchQuery.value.trim()}”.`;
  return "Nobody matches the current filters.";
});

// Just the ids, in display order. Recomputed on every sort mode change, but that's cheap:
// sorting person records, not rebuilding anyone's segments.
//
// A live search takes the sort mode's place while it's running: scoring the matches is
// only worth doing if the best one is at the top, and "region, then name" isn't what
// anyone wants back when they've typed most of a name. Clearing the box hands the order
// straight back to the chosen mode.
const visibleOrder = computed(() => {
  const scores = searchScores.value;
  const rank = scores === null ? primaryRank() : (p: PersonDto) => -(scores.get(p.id) ?? 0);

  return [...filteredPeople.value]
    .sort((a, b) => rank(a) - rank(b) || personLabel(a).localeCompare(personLabel(b)))
    .map((p) => p.id);
});

// The whole team in the order the current sort mode puts them, including everyone the
// region filter or the search box is hiding. Deliberately the same ranking visibleOrder
// uses, just without the filtering, so the rows on screen are a subsequence of this and a
// drop lands where it looks like it will.
//
// primaryRank already reads customOrder when that's the active mode, so there's no separate
// "saved order first" step here. Leading with the saved order regardless of mode was a bug:
// with a custom order left over from an earlier drag and the grid since switched back to
// sort-by-name, one drag would reshuffle every row into that stale order instead of moving
// the row you dragged.
function fullOrder(): number[] {
  const rank = primaryRank();
  return [...people.value]
    .sort((a, b) => rank(a) - rank(b) || personLabel(a).localeCompare(personLabel(b)))
    .map((p) => p.id);
}

// Reordering works on the full team, not on the rows you can currently see. Seeding this
// from the visible order instead used to write that subset back as the entire custom
// order, so one drag while filtering silently threw away the saved position of everyone
// hidden. Hidden people sit between the two rows in the full order, so dropping onto a row
// still lands exactly where it looks like it will.
function handleReorder(personId: number, targetPersonId: number) {
  const order = fullOrder();
  const fromIndex = order.indexOf(personId);
  const toIndex = order.indexOf(targetPersonId);
  if (fromIndex === -1 || toIndex === -1 || fromIndex === toIndex) return;

  order.splice(toIndex, 0, ...order.splice(fromIndex, 1));
  customOrder.value = order;
  writeCache("teamCustomOrder", order);
  sortMode.value = "custom";
}

// Ctrl+K (Cmd+K on a Mac, where Ctrl+K is a different habit entirely) jumps to the search
// box from anywhere on the page. Bound on the window rather than the input for the obvious
// reason - the point is to reach it without clicking it first - and preventDefault because
// the browser's own Ctrl+K would otherwise steal focus to the address bar.
const searchInput = ref<HTMLInputElement | null>(null);

function onSearchShortcut(event: KeyboardEvent) {
  // Lower-cased because Caps Lock (or Shift) makes this "K", and bailing on that would
  // leave the browser's own Ctrl+K to take focus to the address bar instead.
  if (event.key.toLowerCase() !== "k" || !(event.ctrlKey || event.metaKey) || event.altKey) return;
  // A modal owns the keyboard while it's up. Yanking focus out of a half-filled absence
  // form, to a search box the user can't even see behind the modal, is worse than the
  // shortcut simply not firing.
  if (showAbsenceModal.value || showHolidayModal.value || showWhosOffModal.value) return;
  event.preventDefault();
  // A header popover is a menu, not a modal, so the shortcut still goes through - but it
  // takes the panel down on the way. Leaving it open would strand a floating panel over
  // the header with the focus it was holding now gone, and only a click outside or Escape
  // would shift it. Closing one already shut is a no-op.
  sortPopover.value?.close();
  regionPopover.value?.close();
  searchInput.value?.focus();
  searchInput.value?.select();
}

onMounted(() => window.addEventListener("keydown", onSearchShortcut));
onUnmounted(() => window.removeEventListener("keydown", onSearchShortcut));

// Spelled out in the placeholder, since a shortcut nobody can see is a shortcut nobody
// uses. Named for the key the reader's own machine actually wants: the handler above takes
// either modifier, but telling a Mac user to press Ctrl would just be wrong.
const searchShortcut = navigator.userAgent.includes("Mac") ? "⌘K" : "Ctrl K";

// Picking a sort mode is a one-off choice, so the panel gets out of the way afterwards.
// The region checkboxes deliberately don't do this - those are picked several at a time.
// Both are held here regardless, so the search shortcut above can dismiss whichever is up.
const sortPopover = ref<InstanceType<typeof IconPopover> | null>(null);
const regionPopover = ref<InstanceType<typeof IconPopover> | null>(null);

function selectSort(mode: SortMode) {
  sortMode.value = mode;
  sortPopover.value?.close();
}

function goToToday() {
  year.value = new Date().getFullYear();
  nextTick(() => calendarGridRef.value?.scrollToToday());
}

const isLatestYearLoad = useStaleGuard();

// Bump the version suffix whenever AbsenceDto/BankHolidayDto's shape changes: the key
// carries it, so an old browser's copy simply misses and refetches instead of being
// trusted with fields it predates (the incident stores/config.ts describes). Absences v2
// dropped `status`.
const ABSENCES_CACHE_GROUP = "absences:";
const HOLIDAYS_CACHE_GROUP = "holidays:";
const absencesCacheKey = (y: number) => `${ABSENCES_CACHE_GROUP}${y}.v2`;
const holidaysCacheKey = (y: number) => `${HOLIDAYS_CACHE_GROUP}${y}.v1`;

// One cached year per type: the sweep before the write is also what evicts other years
// and any stale schema version, since every key shares its group's prefix.
function writeYearCache() {
  clearCacheGroup(ABSENCES_CACHE_GROUP);
  clearCacheGroup(HOLIDAYS_CACHE_GROUP);
  writeCache(absencesCacheKey(year.value), absences.value);
  writeCache(holidaysCacheKey(year.value), holidays.value);
}

async function loadYear() {
  const isCurrent = isLatestYearLoad();
  const { from, to } = yearRange.value;

  const cachedAbsences = readCache<AbsenceDto[]>(absencesCacheKey(year.value));
  const cachedHolidays = readCache<BankHolidayDto[]>(holidaysCacheKey(year.value));
  const fromCache = cachedAbsences !== null && cachedHolidays !== null;
  if (fromCache) {
    // Paint straight from the cache and revalidate behind it: no skeleton, and the
    // reconcile below patches in only what actually changed since the cache was written.
    // Reconciled rather than assigned, for the callers that reuse loadYear as a
    // revalidate-in-place (recurring holiday saves): the parsed copy is content-equal to
    // what's already on screen, and handing the grid fresh identities for it would
    // rebuild every row just to show the same pixels.
    absences.value = reconcileList(absences.value, cachedAbsences);
    holidays.value = reconcileList(holidays.value, cachedHolidays);
  } else {
    loading.value = true;
  }

  try {
    const [absencesRes, holidaysRes] = await Promise.all([
      getAbsences(from, to),
      getCatalogHolidays(from, to),
    ]);
    if (!isCurrent()) return;
    // reconcileList hands back the previous objects (and, when nothing changed at all,
    // the previous arrays) wherever the fresh copies are field-for-field equal, so a
    // revalidate that confirms the cache re-renders nothing: the grid's segment cache and
    // v-memo compare exactly these identities. See lib/reconcile.ts.
    const nextAbsences = reconcileList(absences.value, absencesRes.absences);
    const nextHolidays = reconcileList(holidays.value, holidaysRes.holidays);
    // The expected steady state is a revalidate that confirms the cache; skipping the
    // rewrite then saves two localStorage sweeps and two year-sized JSON.stringify calls
    // that would store bytes identical to what readCache just parsed. isCurrent above
    // also guards the write: year.value still is the year this call captured, since a
    // change would have started a newer loadYear.
    const unchanged =
      fromCache && nextAbsences === absences.value && nextHolidays === holidays.value;
    absences.value = nextAbsences;
    holidays.value = nextHolidays;
    if (!unchanged) writeYearCache();
  } catch (error: unknown) {
    console.error("ERROR => Failed to load absences/holidays for year:", error);
  } finally {
    if (isCurrent()) loading.value = false;
  }
}

watch(yearRange, loadYear, { immediate: true });

onMounted(() => {
  usePeopleStore()
    .loadOnce()
    .catch((error: unknown) => {
      console.error("ERROR => Failed to load people:", error);
    });
  configStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load app config:", error);
  });
});

// Saving and deleting no longer reload the year: one absence edit cannot change the
// bank-holiday catalog or anyone else's row, and the mutation endpoints return the
// affected record, so the response is patched in directly. The grid's per-person cell
// cache then rebuilds exactly one row instead of the whole grid.
function resetAbsenceModal() {
  showAbsenceModal.value = false;
  dayAbsences.value = [];
  prefillDate.value = null;
}

function handleAbsenceSaved(saved: AbsenceDto) {
  resetAbsenceModal();
  const { from, to } = yearRange.value;
  const next = absences.value.filter((a) => a.id !== saved.id);
  // A save dated entirely outside the viewed year has no cell to paint here, and the
  // year that owns it refetches on entry anyway. Same overlap rule the API's date filter
  // uses; plain string compares, both sides ISO dates.
  if (saved.endDate >= from && saved.startDate <= to) next.push(saved);
  absences.value = next;
  writeYearCache();
}

function handleAbsenceDeleted(id: number) {
  resetAbsenceModal();
  absences.value = absences.value.filter((a) => a.id !== id);
  writeYearCache();
}

function resetHolidayModal() {
  showHolidayModal.value = false;
  editingHoliday.value = null;
  prefillHolidayDate.value = null;
}

function handleHolidaySaved(saved: BankHolidayDto) {
  resetHolidayModal();
  // A recurring holiday can't be patched in from the response: the catalog projects its
  // stored month/day onto the viewed year (see BankHolidayService), and replicating that
  // projection here, leap days and all, isn't worth it for a rare admin action. The
  // reload keeps the current data on screen (the cache hit skips the skeleton) and the
  // refetch reconciles the projected entry in.
  if (saved.isRecurringAnnually) {
    loadYear();
    return;
  }
  const { from, to } = yearRange.value;
  const next = holidays.value.filter((h) => h.id !== saved.id);
  if (saved.date >= from && saved.date <= to) next.push(saved);
  holidays.value = next;
  writeYearCache();
}

function handleHolidayDeleted(id: number) {
  resetHolidayModal();
  // Works for recurring holidays too: the viewed year holds at most one projected entry,
  // and it carries the stored record's id.
  holidays.value = holidays.value.filter((h) => h.id !== id);
  writeYearCache();
}
</script>
