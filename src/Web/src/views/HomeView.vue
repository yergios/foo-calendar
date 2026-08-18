<template>
  <div
    class="w-[min(100vw,1920px)] ml-[calc(50%-min(50vw,960px))] px-6 h-full flex flex-col gap-4 min-h-0"
  >
    <header class="flex items-center justify-between flex-wrap gap-3">
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="h-9 w-42 flex items-center justify-center bg-primary text-on-primary border border-transparent rounded-8 px-3 text-sm font-semibold hover:bg-primary-hover transition-colors disabled:opacity-40 disabled:hover:bg-primary disabled:hover:shadow-none"
          :disabled="!personStore.person"
          :title="!personStore.person ? 'Set up your profile first!' : undefined"
          @click="openAddAbsence()"
        >
          + Add absence
        </button>
        <button
          type="button"
          class="h-9 w-42 flex items-center justify-center border border-accent text-accent rounded-8 px-3 text-sm font-semibold hover:bg-accent/10 transition-colors disabled:opacity-40 disabled:hover:bg-background"
          :disabled="!personStore.person"
          :title="!personStore.person ? 'Set up your profile first!' : undefined"
          @click="openAddBankHoliday()"
        >
          + Add bank holiday
        </button>
      </div>

      <div class="flex items-center gap-3 flex-wrap">
        <div class="flex items-center gap-1.5">
          <label for="sortMode" class="text-xs font-semibold text-secondary">Sort by</label>
          <select
            id="sortMode"
            v-model="sortMode"
            class="h-9 border border-border-strong rounded-8 px-2 text-sm bg-background"
          >
            <option value="name">Name</option>
            <option value="mine">Me first, then name</option>
            <option value="absentToday">Absent today, then name</option>
            <option value="region">Region, then name</option>
            <option value="custom">Custom order</option>
          </select>
        </div>

        <div class="flex items-center gap-1.5">
          <label for="regionFilter" class="text-xs font-semibold text-secondary">Region</label>
          <RegionFilterSelect id="regionFilter" v-model="regionFilter" :groups="regionGroups" />
        </div>

        <YearPicker v-model="year">
          <span class="text-sm font-semibold px-1 tabular-nums">{{ year }}</span>
        </YearPicker>
      </div>
    </header>

    <CalendarGrid
      ref="calendarGridRef"
      :people="filteredPeople"
      :order="visibleOrder"
      :absences="absences"
      :holidays="holidays"
      :year="year"
      :loading="loading"
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
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-accent-solid text-on-accent text-[8px] font-bold"
            >{{ initial }}</span
          >
          <span>{{ label }}</span>
        </div>
        <div
          v-if="configStore.config?.absenceApprovalsEnabled"
          class="flex items-center gap-1.5"
        >
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-accent-solid text-on-accent text-[8px] font-bold"
            >?</span
          >
          <span>Pending approval</span>
        </div>
      </div>

      <button
        type="button"
        class="text-sm font-semibold text-accent border border-accent rounded-pill px-3.5 py-1.5 hover:bg-accent/10 transition-colors"
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
      @saved="closeAbsenceModal"
      @deleted="closeAbsenceModal"
    />
    <AddBankHolidayModal
      v-if="showHolidayModal"
      :holiday="editingHoliday"
      :initial-date="prefillHolidayDate"
      @close="showHolidayModal = false"
      @saved="closeHolidayModal"
      @deleted="closeHolidayModal"
    />
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, nextTick, onMounted, ref, watch } from "vue";

import { getAbsences } from "@/api/absences";
import { getCatalogHolidays } from "@/api/bankHolidays";
import AddAbsenceModal from "@/components/AddAbsenceModal.vue";
import AddBankHolidayModal from "@/components/AddBankHolidayModal.vue";
import CalendarGrid from "@/components/CalendarGrid.vue";
import RegionFilterSelect from "@/components/RegionFilterSelect.vue";
import YearPicker from "@/components/YearPicker.vue";
import { readCache, writeCache } from "@/lib/browserCache";
import { toIso, today } from "@/lib/dates";
import { groupRegions } from "@/lib/regionGroups";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import { usePersonStore } from "@/stores/person";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

type SortMode = "name" | "mine" | "absentToday" | "region" | "custom";

const { people, regionLabels } = storeToRefs(usePeopleStore());
const personStore = usePersonStore();
const configStore = useConfigStore();

// The filter offers exactly where the team is, which the API sends alongside the people it
// is filtering. Deriving it rather than reading a configured list is what lets someone
// joining a new region show up here without a redeploy.
const regionGroups = computed(() => groupRegions(regionLabels.value));

const year = ref(new Date().getFullYear());
const absences = ref<AbsenceDto[]>([]);
const holidays = ref<BankHolidayDto[]>([]);
const loading = ref(false);
const calendarGridRef = ref<InstanceType<typeof CalendarGrid> | null>(null);
const showAbsenceModal = ref(false);
const showHolidayModal = ref(false);
const prefillDate = ref<string | null>(null);
const prefillHolidayDate = ref<string | null>(null);
const dayAbsences = ref<AbsenceDto[]>([]);
const editingHoliday = ref<BankHolidayDto | null>(null);

const sortMode = ref<SortMode>(readCache<SortMode>("teamSortMode") ?? "name");
// Array now, not a single code: coerced defensively since an older cached value here
// (from before multi-select) was a plain string, not an array.
const cachedRegionFilter = readCache<string[]>("teamRegionFilter");
const regionFilter = ref<string[]>(Array.isArray(cachedRegionFilter) ? cachedRegionFilter : []);
const customOrder = ref<number[]>(readCache<number[]>("teamCustomOrder") ?? []);

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

// Filtering only, no sorting: CalendarGrid keys its (expensive, a year of cells per
// person) content off this list, so switching sort mode - which only ever changes display
// order, never membership - doesn't force it to rebuild that content from scratch. See
// visibleOrder below for the cheap part that does change with sort mode.
const filteredPeople = computed(() => people.value.filter((p) => matchesRegionFilter(p.homeCity)));

// Just the ids, in display order. Recomputed on every sort mode change, but that's cheap:
// sorting person records, not rebuilding anyone's cells.
const visibleOrder = computed(() => {
  const rank = primaryRank();
  return [...filteredPeople.value]
    .sort((a, b) => rank(a) - rank(b) || personLabel(a).localeCompare(personLabel(b)))
    .map((p) => p.id);
});

// Reordering starts from the order you currently see, not the saved customOrder array
// (that one can be empty or out of date for newer team members). That way a drag
// always works, no matter which sort mode was active beforehand.
function handleReorder(personId: number, targetPersonId: number) {
  const order = [...visibleOrder.value];
  const fromIndex = order.indexOf(personId);
  const toIndex = order.indexOf(targetPersonId);
  if (fromIndex === -1 || toIndex === -1 || fromIndex === toIndex) return;

  order.splice(toIndex, 0, ...order.splice(fromIndex, 1));
  customOrder.value = order;
  writeCache("teamCustomOrder", order);
  sortMode.value = "custom";
}

function goToToday() {
  year.value = new Date().getFullYear();
  nextTick(() => calendarGridRef.value?.scrollToToday());
}

async function loadYear() {
  loading.value = true;
  try {
    const { from, to } = yearRange.value;
    const [absencesRes, holidaysRes] = await Promise.all([
      getAbsences(from, to),
      getCatalogHolidays(from, to),
    ]);
    absences.value = absencesRes.absences;
    holidays.value = holidaysRes.holidays;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load absences/holidays for year:", error);
  } finally {
    loading.value = false;
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

// Saving and deleting leave the same thing to do: drop the modal state and reload the
// year. The reloaded data is what tells the grid which of the two actually happened.
function closeAbsenceModal() {
  showAbsenceModal.value = false;
  dayAbsences.value = [];
  prefillDate.value = null;
  loadYear();
}

function closeHolidayModal() {
  showHolidayModal.value = false;
  editingHoliday.value = null;
  prefillHolidayDate.value = null;
  loadYear();
}
</script>
