<template>
  <section>
    <h4>Bank holidays</h4>
    <p class="text-sm text-secondary mt-1">
      Pulls public holidays for the regions the team occupies. Picking a single region re-runs
      just that one, which is what a region listed as failed needs.
    </p>

    <div class="flex items-center gap-3 mt-3">
      <input
        v-model.number="syncYear"
        type="number"
        class="border border-border-strong rounded-8 px-3 py-2 w-28"
      />
      <select
        v-model="syncRegion"
        class="border border-border-strong rounded-8 px-3 py-2 bg-background text-sm"
      >
        <option :value="null">All regions</option>
        <optgroup v-for="country in regionGroups" :key="country.code" :label="country.label">
          <option :value="country.code">{{ country.label }} (nationwide)</option>
          <option v-for="opt in country.regions" :key="opt.code" :value="opt.code">
            {{ opt.label }}
          </option>
        </optgroup>
      </select>
      <button
        type="button"
        class="bg-primary text-on-primary border border-transparent rounded-8 px-4 py-2 text-sm transition-all duration-150 ease-out hover:bg-primary-hover disabled:opacity-40"
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
      <p class="text-secondary">Synced: {{ syncResult.syncedRegions.join(", ") || "none" }}</p>
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
import { groupRegions } from "@/lib/regionGroups";
import { usePeopleStore } from "@/stores/people";
import type { SyncBankHolidaysResponse } from "@/types/bankHoliday";

// The regions worth offering a re-sync for are the ones the team occupies, which is
// exactly what the scheduled sync covers and what the API will accept here.
const peopleStore = usePeopleStore();
const { regionLabels } = storeToRefs(peopleStore);
const regionGroups = computed(() => groupRegions(regionLabels.value));

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
