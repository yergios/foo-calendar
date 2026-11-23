<template>
  <AppModal title="Who's off today" size="md" @close="$emit('close')">
    <p class="text-xs text-secondary -mt-1 mb-3">
      {{ todayLabel
      }}<span v-if="configStore.config"> ({{ configStore.config.homeTimeZone }})</span>
    </p>

    <p v-if="loading" class="text-sm text-secondary py-2">Loading…</p>
    <p v-else-if="error" class="text-sm text-error">{{ error }}</p>
    <p v-else-if="entries.length === 0" class="text-sm text-secondary py-2">Nobody's off today.</p>

    <ul v-else class="divide-y divide-border">
      <li v-for="entry in entries" :key="entry.key" class="flex items-center gap-3 py-2.5">
        <span
          class="inline-flex shrink-0 items-center justify-center w-5 h-5 rounded-4 text-xs font-bold"
          :class="BADGE_CLASS[entry.kind]"
          >{{ entry.initial }}</span
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
import { formatLongDate, today } from "@/lib/dates";
import { type OffTodayEntry, horizonFrom, whosOffToday } from "@/lib/whosOffToday";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import type { AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";

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

// Both reach forward to the horizon the "back on" dates need (see whosOffToday.ts), fetched
// in one go so this still costs a single round trip.
onMounted(async () => {
  try {
    const horizonIso = horizonFrom(todayIso);
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

// Same badge look CalendarGrid's own legend uses, so "off for a personal reason" and "off
// for a bank holiday" read as the same two categories everywhere in the app.
const BADGE_CLASS: Record<OffTodayEntry["kind"], string> = {
  absence: "bg-accent-solid text-on-accent",
  holiday: "bg-holiday border border-border-strong text-ink",
};

const entries = computed(() =>
  whosOffToday(todayIso, people.value, absences.value, holidays.value),
);
</script>
