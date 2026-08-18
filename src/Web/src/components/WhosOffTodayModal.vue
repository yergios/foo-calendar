<template>
  <AppModal title="Who's off today" size="md" @close="$emit('close')">
    <p class="text-xs text-secondary -mt-1 mb-3">{{ todayLabel }}</p>

    <p v-if="loading" class="text-sm text-secondary py-2">Loading…</p>
    <p v-else-if="error" class="text-sm text-error">{{ error }}</p>
    <p v-else-if="entries.length === 0" class="text-sm text-secondary py-2">Nobody's off today.</p>

    <ul v-else class="divide-y divide-border">
      <li v-for="entry in entries" :key="entry.key" class="flex items-center gap-3 py-2.5">
        <span
          class="inline-flex shrink-0 items-center justify-center w-5 h-5 rounded-4 text-[10px] font-bold"
          :class="entry.badgeClass"
          >{{ entry.badgeInitial }}</span
        >
        <div class="min-w-0 flex-1">
          <p class="text-sm text-ink truncate">
            {{ entry.person.displayName ?? entry.person.username }}
          </p>
          <p class="text-xs text-secondary truncate">
            {{ entry.typeLabel }}<span v-if="entry.detail"> · {{ entry.detail }}</span>
          </p>
        </div>
      </li>
    </ul>
  </AppModal>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onMounted, ref } from "vue";

import { getAbsences } from "@/api/absences";
import { getCatalogHolidays } from "@/api/bankHolidays";
import AppModal from "@/components/AppModal.vue";
import { formatLongDate, toDisplayTime, today } from "@/lib/dates";
import { usePeopleStore } from "@/stores/people";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto, type AbsenceType } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

defineEmits<{ close: [] }>();

// The whole team, regardless of the home page's own region filter: "who's off today" is a
// global question, not scoped to whatever the grid currently happens to be narrowed to.
const { people } = storeToRefs(usePeopleStore());

// Fetched fresh for exactly today rather than reused from HomeView's year-scoped
// `absences`/`holidays`: those only actually cover today when the grid happens to be
// showing the current year, and this should answer correctly no matter which year is on
// screen.
const loading = ref(true);
const error = ref<string | null>(null);
const absences = ref<AbsenceDto[]>([]);
const holidays = ref<BankHolidayDto[]>([]);

const todayIso = today();
const todayLabel = formatLongDate(todayIso);

onMounted(async () => {
  try {
    const [absencesRes, holidaysRes] = await Promise.all([
      getAbsences(todayIso, todayIso),
      getCatalogHolidays(todayIso, todayIso),
    ]);
    absences.value = absencesRes.absences;
    holidays.value = holidaysRes.holidays;
  } catch (err: unknown) {
    console.error("ERROR => Failed to load today's absences:", err);
    error.value = "Failed to load who's off today. Please try again.";
  } finally {
    loading.value = false;
  }
});

interface Entry {
  key: string;
  person: PersonDto;
  badgeInitial: string;
  badgeClass: string;
  typeLabel: string;
  /** Time range for a time-based absence, the holiday's name, or "" for a whole-day one. */
  detail: string;
  category: number;
}

// Same badge look CalendarGrid's own legend uses, so "off for a personal reason" and "off
// for a bank holiday" read as the same two categories everywhere in the app.
const ABSENCE_BADGE_CLASS = "bg-accent-solid text-on-accent";
const HOLIDAY_BADGE_CLASS = "bg-holiday border border-border-strong text-ink";

// The list's sort order: time-based first (whoever's out for just part of the day is the
// one worth knowing about soonest), then everyone off for the whole day, holiday before
// personal reasons. Appointment is never actually whole-day (enforced server-side), so its
// entry here is unreachable in practice but keeps the map exhaustive.
const HOLIDAY_CATEGORY = 1;
const TIME_BASED_CATEGORY = 0;
const WHOLE_DAY_CATEGORY: Record<AbsenceType, number> = {
  Appointment: TIME_BASED_CATEGORY,
  Vacation: 2,
  Sick: 3,
  Other: 4,
};

// A nationwide holiday (no subdivision suffix) covers everyone in the country; a
// subdivision's own holiday covers only people set to that exact subdivision, not
// siblings under the same country. Mirrors the API's RegionCode.BelongsTo.
function holidayAppliesToPerson(holidayRegion: string, personHomeCity: string): boolean {
  if (holidayRegion === personHomeCity) return true;
  const holidayIsNationwide = !holidayRegion.includes("-");
  const personCountry = personHomeCity.split("-")[0] ?? personHomeCity;
  return holidayIsNationwide && personCountry === holidayRegion;
}

// Approved only, same distinction the weekly digest email draws: a still-pending request
// hasn't actually been granted, so it isn't yet "off" in the sense this list means.
const entries = computed<Entry[]>(() => {
  const peopleById = new Map(people.value.map((person) => [person.id, person]));

  const absenceEntries: Entry[] = absences.value
    .filter((absence) => absence.status === "Approved")
    .map((absence): Entry | null => {
      const person = peopleById.get(absence.personId);
      if (!person) return null;
      const detail = absence.startTime
        ? `${toDisplayTime(absence.startTime)}–${toDisplayTime(absence.endTime)}`
        : "";
      return {
        key: `absence-${absence.id}`,
        person,
        badgeInitial: ABSENCE_TYPE_INITIALS[absence.type],
        badgeClass: ABSENCE_BADGE_CLASS,
        typeLabel: absence.type,
        detail,
        category: absence.startTime ? TIME_BASED_CATEGORY : WHOLE_DAY_CATEGORY[absence.type],
      };
    })
    .filter((entry): entry is Entry => entry !== null);

  // Anyone already listed for a personal absence today doesn't also need a "their region
  // happens to have a holiday" line - one reason they're off is enough.
  const alreadyListed = new Set(absenceEntries.map((entry) => entry.person.id));

  const holidayEntries: Entry[] = people.value.flatMap((person) => {
    const homeCity = person.homeCity;
    if (alreadyListed.has(person.id) || !homeCity) return [];
    const holiday = holidays.value.find((h) => holidayAppliesToPerson(h.region, homeCity));
    if (!holiday) return [];
    return [
      {
        key: `holiday-${person.id}`,
        person,
        badgeInitial: "H",
        badgeClass: HOLIDAY_BADGE_CLASS,
        typeLabel: "Bank holiday",
        detail: holiday.name,
        category: HOLIDAY_CATEGORY,
      },
    ];
  });

  return [...absenceEntries, ...holidayEntries].sort((a, b) => {
    if (a.category !== b.category) return a.category - b.category;
    const label = (p: PersonDto) => (p.displayName ?? p.username).toLowerCase();
    return label(a.person).localeCompare(label(b.person));
  });
});
</script>
