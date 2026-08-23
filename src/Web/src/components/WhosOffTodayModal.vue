<template>
  <AppModal title="Who's off today" size="md" @close="$emit('close')">
    <p class="text-xs text-secondary -mt-1 mb-3">
      {{ todayLabel }}<span v-if="configStore.config"> ({{ configStore.config.homeTimeZone }})</span>
    </p>

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
import {
  addDays,
  formatDayMonth,
  formatLongDate,
  nextWorkingDay,
  toDisplayTime,
  today,
} from "@/lib/dates";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto, type AbsenceType } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

defineEmits<{ close: [] }>();

const configStore = useConfigStore();

// Same reasoning as AddAbsenceModal's own onMounted: nothing guarantees another view
// already loaded this before the modal opens, and loadOnce() dedupes so it's never a
// second round trip when one did.
onMounted(() => {
  configStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load app config:", error);
  });
});

// The whole team, regardless of the home page's own region filter: "who's off today" is a
// global question, not scoped to whatever the grid currently happens to be narrowed to.
const { people } = storeToRefs(usePeopleStore());

// Fetched fresh rather than reused from HomeView's year-scoped `absences`/`holidays`: those
// only actually cover today when the grid happens to be showing the current year, and this
// should answer correctly no matter which year is on screen.
const loading = ref(true);
const error = ref<string | null>(null);
const absences = ref<AbsenceDto[]>([]);
const holidays = ref<BankHolidayDto[]>([]);

const todayIso = today();
const todayLabel = formatLongDate(todayIso);

// Both fetches reach forward rather than asking about today alone, because the "back on"
// date has to skip whatever else is already in the way: a bank holiday, and any absence
// that picks up where this one leaves off. Wide enough to cover the return date of any
// realistic absence someone is in the middle of, and both are fetched in one go so this
// still costs a single round trip. Anything running past the horizon falls back to
// skipping weekends only.
const HORIZON_DAYS = 120;
const horizonIso = addDays(todayIso, HORIZON_DAYS);

onMounted(async () => {
  try {
    const [absencesRes, holidaysRes] = await Promise.all([
      getAbsences(todayIso, horizonIso),
      getCatalogHolidays(todayIso, horizonIso),
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
  /** Time range for a time-based absence, the return date for a whole-day one, or the
   * holiday's name. */
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

// Every date each person is already booked off again, from the approved whole-day absences
// in the fetched window. A second absence starting the day after the first ends is
// perfectly legal - the server rejects overlapping absences, not adjacent ones - so without
// this the "back on" date would announce a return that the next absence immediately
// cancels. Time-based absences are left out on purpose: an appointment doesn't stop someone
// being back that day.
const wholeDayAbsenceDates = computed(() => {
  const byPerson = new Map<number, Set<string>>();

  for (const absence of absences.value) {
    if (absence.status !== "Approved" || absence.startTime) continue;

    let dates = byPerson.get(absence.personId);
    if (dates === undefined) {
      dates = new Set();
      byPerson.set(absence.personId, dates);
    }
    // Capped at the horizon rather than the absence's own end: a year-long leave would
    // otherwise expand into hundreds of entries nobody reads past the first few of.
    for (let iso = absence.startDate; iso <= absence.endDate && iso <= horizonIso; ) {
      dates.add(iso);
      iso = addDays(iso, 1);
    }
  }

  return byPerson;
});

// What stops a given date counting as this person's first day back: their own region's
// bank holidays, and any whole-day absence already covering it. Someone with no home city
// set has no holiday calendar to check against, so only the absences apply. The linear scan
// is over one horizon's worth of holidays and runs a handful of times per person, so an
// index per person would cost more than it saves.
function unavailableCheckFor(person: PersonDto): (iso: string) => boolean {
  const homeCity = person.homeCity;
  const bookedOff = wholeDayAbsenceDates.value.get(person.id);

  return (iso) =>
    bookedOff?.has(iso) === true ||
    (homeCity !== null &&
      holidays.value.some((h) => h.date === iso && holidayAppliesToPerson(h.region, homeCity)));
}

// Approved only, same distinction the weekly digest email draws: a still-pending request
// hasn't actually been granted, so it isn't yet "off" in the sense this list means.
const entries = computed<Entry[]>(() => {
  const peopleById = new Map(people.value.map((person) => [person.id, person]));

  const absenceEntries: Entry[] = absences.value
    // The date check matters now that the fetch reaches forward for the "back on" dates:
    // an absence starting next month must not put anyone on today's list.
    .filter(
      (absence) =>
        absence.status === "Approved" &&
        absence.startDate <= todayIso &&
        absence.endDate >= todayIso,
    )
    .map((absence): Entry | null => {
      const person = peopleById.get(absence.personId);
      if (!person) return null;
      const backOn = nextWorkingDay(absence.endDate, unavailableCheckFor(person));
      // The year is worth spelling out once the return date crosses into the next one:
      // "02/01" on its own reads as a date that has already been and gone.
      const detail = absence.startTime
        ? `${toDisplayTime(absence.startTime)}–${toDisplayTime(absence.endTime)}`
        : `Back on the ${formatDayMonth(backOn, backOn.slice(0, 4) !== todayIso.slice(0, 4))}`;
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
    // The date check matters now that the fetch reaches past today for the "back on" dates:
    // a holiday three weeks out must not put anyone on today's list.
    const holiday = holidays.value.find(
      (h) => h.date === todayIso && holidayAppliesToPerson(h.region, homeCity),
    );
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
