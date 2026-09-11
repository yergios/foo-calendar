<template>
  <section>
    <h4>Bank holidays</h4>
    <p class="text-sm text-secondary mt-1">
      Pulls public holidays for the regions the team occupies. Picking a single region re-runs just
      that one, which is what a region listed as failed needs.
    </p>

    <div class="flex items-center gap-3 mt-3">
      <input
        v-model.number="syncYear"
        type="number"
        class="h-10 border border-border-strong rounded-8 px-3 w-28"
      />
      <RegionSelect
        id="bankHolidaySyncRegion"
        v-model="syncRegion"
        :options="syncRegionOptions"
        empty-label="All regions"
        placeholder="Search regions…"
        class="w-64"
      />
      <button
        type="button"
        class="h-10 bg-primary text-on-primary border border-transparent rounded-8 px-4 text-sm transition-all hover:bg-primary-hover disabled:opacity-40"
        :disabled="syncing"
        @click="handleSync"
      >
        {{ syncing ? "Syncing…" : "Sync" }}
      </button>
    </div>

    <p v-if="syncError" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mt-3">
      {{ syncError }}
    </p>
    <div v-if="syncResult" class="text-sm mt-3">
      <SuccessMessage v-if="syncResult.syncedRegions.length > 0">
        Synced: {{ syncResult.syncedRegions.join(", ") }}
      </SuccessMessage>
      <p v-else class="text-secondary">No regions were synced.</p>
      <p v-if="syncResult.failedRegions.length > 0" class="text-error mt-1">
        Failed: {{ syncResult.failedRegions.join(", ") }}
      </p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onMounted, ref } from "vue";

import { syncBankHolidays } from "@/api/admin";
import RegionSelect, { type RegionSelectOption } from "@/components/RegionSelect.vue";
import SuccessMessage from "@/components/SuccessMessage.vue";
import { groupRegions } from "@/lib/regionGroups";
import { usePeopleStore } from "@/stores/people";
import type { SyncBankHolidaysResponse } from "@/types/bankHoliday";

// The regions worth offering a re-sync for are the ones the team occupies, which is
// exactly what the scheduled sync covers and what the API will accept here.
const peopleStore = usePeopleStore();
const { regionLabels } = storeToRefs(peopleStore);
const regionGroups = computed(() => groupRegions(regionLabels.value));

// RegionSelect wants one flat, already-searchable list rather than regionGroups' nested
// shape, but each subdivision's `group` keeps it rendered indented under its own country -
// same country/subdivision hierarchy RegionFilterSelect groups by, just single-pick and
// searchable instead of checkboxes. The "(nationwide)" suffix is what keeps a country's own
// entry (which has no `group` - it isn't indented under itself) distinguishable from them.
const syncRegionOptions = computed<RegionSelectOption[]>(() =>
  regionGroups.value.flatMap((country) => [
    { code: country.code, label: `${country.label} (nationwide)` },
    ...country.regions.map((region) => ({ ...region, group: country.label })),
  ]),
);

onMounted(() => {
  peopleStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load people:", error);
  });
});

const syncYear = ref(new Date().getFullYear());
const syncRegion = ref<string | null>(null);
const syncing = ref(false);
const syncError = ref<string | null>(null);
const syncResult = ref<SyncBankHolidaysResponse | null>(null);

async function handleSync() {
  syncing.value = true;
  syncError.value = null;
  syncResult.value = null;
  try {
    syncResult.value = await syncBankHolidays(syncYear.value, syncRegion.value);
  } catch (error: unknown) {
    console.error("ERROR => Failed to sync bank holidays:", error);
    syncError.value = error instanceof Error ? error.message : "Failed to sync bank holidays.";
  } finally {
    syncing.value = false;
  }
}
</script>
