<template>
  <div
    class="w-[min(100vw,1920px)] ml-[calc(50%-min(50vw,960px))] px-6 h-full flex flex-col gap-4 min-h-0"
  >
    <header class="flex items-center justify-between flex-wrap gap-3">
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="w-42 text-center bg-primary text-white border border-transparent rounded-8 px-3 py-2 text-sm font-semibold hover:bg-primary-hover transition-colors disabled:opacity-40 disabled:hover:bg-primary disabled:hover:shadow-none"
          :disabled="!personStore.person"
          :title="!personStore.person ? 'Set up your profile first!' : undefined"
          @click="openAddAbsence()"
        >
          + Add absence
        </button>
        <button
          type="button"
          class="w-42 text-center border border-accent text-accent rounded-8 px-3 py-2 text-sm font-semibold hover:bg-accent/10 transition-colors disabled:opacity-40 disabled:hover:bg-background"
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
            class="border border-border rounded-8 px-2 py-1 text-sm bg-background"
          >
            <option value="name">Name</option>
            <option value="absentToday">Absent today, then name</option>
            <option value="custom">Custom order</option>
          </select>
        </div>

        <div class="flex items-center gap-1.5">
          <label for="regionFilter" class="text-xs font-semibold text-secondary">Region</label>
          <select
            id="regionFilter"
            v-model="regionFilter"
            class="border border-border rounded-8 px-2 py-1 text-sm bg-background"
          >
            <option value="">All regions</option>
            <optgroup v-for="country in regionCountries" :key="country.code" :label="country.name">
              <option :value="country.code">{{ country.name }} (all)</option>
              <option v-for="opt in country.regions" :key="opt.code" :value="opt.code">
                {{ opt.label }}
              </option>
            </optgroup>
          </select>
        </div>

        <YearPicker v-model="year">
          <h4>{{ year }}</h4>
        </YearPicker>
      </div>
    </header>

    <CalendarGrid
      ref="calendarGridRef"
      :people="visiblePeople"
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
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-holiday border border-border"
          />
          <span>Bank holiday</span>
        </div>
        <div class="flex items-center gap-1.5">
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-accent text-white text-[8px] font-bold"
            >V</span
          >
          <span>Vacation</span>
        </div>
        <div class="flex items-center gap-1.5">
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-accent text-white text-[8px] font-bold"
            >S</span
          >
          <span>Sick</span>
        </div>
        <div class="flex items-center gap-1.5">
          <span
            class="inline-flex shrink-0 items-center justify-center w-4 h-4 rounded-4 bg-accent text-white text-[8px] font-bold"
            >O</span
          >
          <span>Other</span>
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
      :absence="editingAbsence"
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
import YearPicker from "@/components/YearPicker.vue";
import { useDateFormat } from "@/composables/useDateFormat";
import { readCache, writeCache } from "@/lib/browserCache";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import { usePersonStore } from "@/stores/person";
import type { AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

type SortMode = "name" | "absentToday" | "custom";

const { toIso } = useDateFormat();
const { people } = storeToRefs(usePeopleStore());
const personStore = usePersonStore();
const configStore = useConfigStore();
const regionCountries = computed(() => configStore.config?.regions ?? []);

const year = ref(new Date().getFullYear());
const absences = ref<AbsenceDto[]>([]);
const holidays = ref<BankHolidayDto[]>([]);
const loading = ref(false);
const calendarGridRef = ref<InstanceType<typeof CalendarGrid> | null>(null);
const showAbsenceModal = ref(false);
const showHolidayModal = ref(false);
const prefillDate = ref<string | null>(null);
const prefillHolidayDate = ref<string | null>(null);
const editingAbsence = ref<AbsenceDto | null>(null);
const editingHoliday = ref<BankHolidayDto | null>(null);

const sortMode = ref<SortMode>(readCache<SortMode>("teamSortMode") ?? "name");
const regionFilter = ref(readCache<string>("teamRegionFilter") ?? "");
const customOrder = ref<number[]>(readCache<number[]>("teamCustomOrder") ?? []);

watch(sortMode, (mode) => writeCache("teamSortMode", mode));
watch(regionFilter, (region) => writeCache("teamRegionFilter", region));

const yearRange = computed(() => {
  const from = new Date(year.value, 0, 1);
  const to = new Date(year.value, 11, 31);
  return { from: toIso(from), to: toIso(to) };
});

function openAddAbsence(date: string | null = null) {
  editingAbsence.value = null;
  prefillDate.value = date;
  showAbsenceModal.value = true;
}

function openEditAbsence(absence: AbsenceDto) {
  editingAbsence.value = absence;
  prefillDate.value = null;
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

function isAbsentToday(personId: number): boolean {
  const todayIso = toIso(new Date());
  return absences.value.some(
    (a) => a.personId === personId && a.startDate <= todayIso && a.endDate >= todayIso,
  );
}

// A region filter matches its own code exactly, or any subdivision under it. So if
// you pick a country like "CH", you also get people set to "CH-ZH", "CH-BE", etc.
function matchesRegionFilter(homeCity: string | null): boolean {
  if (!regionFilter.value) return true;
  if (!homeCity) return false;
  return homeCity === regionFilter.value || homeCity.startsWith(`${regionFilter.value}-`);
}

const visiblePeople = computed(() => {
  const filtered = people.value.filter((p) => matchesRegionFilter(p.homeCity));

  if (sortMode.value === "custom" && customOrder.value.length > 0) {
    const orderIndex = new Map(customOrder.value.map((id, index) => [id, index]));
    return [...filtered].sort((a, b) => {
      const ai = orderIndex.get(a.id) ?? Number.MAX_SAFE_INTEGER;
      const bi = orderIndex.get(b.id) ?? Number.MAX_SAFE_INTEGER;
      return ai !== bi ? ai - bi : personLabel(a).localeCompare(personLabel(b));
    });
  }

  if (sortMode.value === "absentToday") {
    return [...filtered].sort((a, b) => {
      const diff = Number(isAbsentToday(b.id)) - Number(isAbsentToday(a.id));
      return diff !== 0 ? diff : personLabel(a).localeCompare(personLabel(b));
    });
  }

  return [...filtered].sort((a, b) => personLabel(a).localeCompare(personLabel(b)));
});

// Reordering starts from the order you currently see, not the saved customOrder array
// (that one can be empty or out of date for newer team members). That way a drag
// always works, no matter which sort mode was active beforehand.
function handleReorder(personId: number, targetPersonId: number) {
  const order = visiblePeople.value.map((p) => p.id);
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

function handleAbsenceSaved() {
  showAbsenceModal.value = false;
  editingAbsence.value = null;
  prefillDate.value = null;
  loadYear();
}

function handleAbsenceDeleted() {
  showAbsenceModal.value = false;
  editingAbsence.value = null;
  loadYear();
}

function handleHolidaySaved() {
  showHolidayModal.value = false;
  editingHoliday.value = null;
  loadYear();
}

function handleHolidayDeleted() {
  showHolidayModal.value = false;
  editingHoliday.value = null;
  loadYear();
}
</script>
