<template>
  <div class="max-w-3xl mx-auto py-8 flex flex-col gap-10">
    <section>
      <div class="flex items-center justify-between">
        <h4>People</h4>
        <YearPicker v-model="statsYear">
          <span class="text-sm text-secondary">Stats for {{ statsYear }}</span>
        </YearPicker>
      </div>

      <p v-if="loadingPeople" class="text-sm text-secondary mt-3">Loading…</p>

      <table v-else-if="people.length > 0" class="w-full mt-3 text-sm">
        <thead>
          <tr class="text-left text-xs font-semibold text-secondary border-b border-border">
            <th class="py-2 pr-2">Person</th>
            <th class="py-2 pr-2">Username</th>
            <th class="py-2 pr-2">Home city</th>
            <th class="py-2 pr-2 text-center">Vacation</th>
            <th class="py-2 pr-2 text-center">Sick</th>
            <th class="py-2 pr-2 text-center">Other</th>
            <th class="py-2 pr-2"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="person in people" :key="person.id" class="border-b border-border">
            <td class="py-2 pr-2">
              <div class="flex items-center gap-2">
                <span
                  class="inline-block w-2.5 h-2.5 rounded-pill shrink-0"
                  :style="{ backgroundColor: person.color }"
                />
                {{ person.displayName ?? person.username }}
              </div>
            </td>
            <td class="py-2 pr-2 text-secondary">{{ person.username }}</td>
            <td class="py-2 pr-2 text-secondary">{{ person.homeCity ?? "—" }}</td>
            <td class="py-2 pr-2 text-center text-secondary">
              {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.vacationDays ?? 0) }}
            </td>
            <td class="py-2 pr-2 text-center text-secondary">
              {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.sickDays ?? 0) }}
            </td>
            <td class="py-2 pr-2 text-center text-secondary">
              {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.otherDays ?? 0) }}
            </td>
            <td class="py-2 pr-2 text-right whitespace-nowrap">
              <button
                type="button"
                class="text-xs font-semibold text-primary hover:text-accent transition-colors"
                @click="startEdit(person)"
              >
                Edit
              </button>
              <button
                type="button"
                class="text-xs font-semibold text-error hover:opacity-70 transition-opacity ml-3"
                :disabled="deletingId === person.id"
                @click="handleDelete(person)"
              >
                {{ deletingId === person.id ? "Deleting…" : "Delete" }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-else class="text-sm text-secondary mt-3">No people yet.</p>

      <div class="mt-6 border border-border rounded-12 p-4">
        <h5>{{ editingId ? "Edit person" : "Add person" }}</h5>
        <div class="grid grid-cols-2 gap-3 mt-3">
          <div v-if="!editingId" class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary"
              >Username<span class="text-error ml-0.5" title="Required">*</span></label
            >
            <input
              v-model="form.username"
              type="text"
              maxlength="100"
              placeholder="e.g. jdoe"
              class="border border-border rounded-8 px-3 py-2"
            />
          </div>
          <div v-if="!editingId" class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary">First name</label>
            <input
              v-model="form.firstName"
              type="text"
              maxlength="100"
              placeholder="e.g. Jane"
              class="border border-border rounded-8 px-3 py-2"
            />
          </div>
          <div v-if="!editingId" class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary">Last name</label>
            <input
              v-model="form.lastName"
              type="text"
              maxlength="100"
              placeholder="e.g. Doe"
              class="border border-border rounded-8 px-3 py-2"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary"
              >Display name<span class="text-error ml-0.5" title="Required">*</span></label
            >
            <input
              v-model="form.displayName"
              type="text"
              maxlength="100"
              placeholder="e.g. Jane D."
              class="border border-border rounded-8 px-3 py-2"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label class="text-xs font-semibold text-secondary">Home city</label>
            <select
              v-model="form.homeCity"
              class="border border-border rounded-8 px-3 py-2 bg-white"
            >
              <option :value="null">Not set</option>
              <optgroup
                v-for="country in regionCountries"
                :key="country.code"
                :label="country.name"
              >
                <option v-for="opt in country.regions" :key="opt.code" :value="opt.code">
                  {{ opt.label }}
                </option>
              </optgroup>
            </select>
          </div>
        </div>

        <div class="flex flex-col gap-1.5 mt-3">
          <label class="text-xs font-semibold text-secondary"
            >Color<span class="text-error ml-0.5" title="Required">*</span></label
          >
          <ColorPicker
            v-model="form.color"
            :palette="palette"
            :colors-in-use="colorsInUse"
            :exclude="editingOriginalColor"
            :loading="configLoading"
          />
        </div>

        <p v-if="formError" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mt-3">
          {{ formError }}
        </p>

        <div class="flex gap-3 mt-4">
          <button
            type="button"
            class="bg-accent text-white rounded-8 px-4 py-2 text-sm transition-all duration-150 ease-out hover:bg-primary disabled:opacity-40"
            :disabled="!canSubmitForm || submittingForm"
            @click="submitForm"
          >
            {{ submittingForm ? "Saving…" : editingId ? "Save changes" : "Add person" }}
          </button>
          <button
            v-if="editingId"
            type="button"
            class="text-sm text-secondary hover:text-primary-dark transition-colors"
            @click="cancelEdit"
          >
            Cancel
          </button>
        </div>
      </div>
    </section>

    <section>
      <h4>Bank holidays</h4>
      <p class="text-sm text-secondary mt-1">
        Pulls public holidays for every configured region from the Nager API for the given year.
      </p>

      <div class="flex items-center gap-3 mt-3">
        <input
          v-model.number="syncYear"
          type="number"
          class="border border-border rounded-8 px-3 py-2 w-28"
        />
        <button
          type="button"
          class="bg-accent text-white rounded-8 px-4 py-2 text-sm transition-all duration-150 ease-out hover:bg-primary disabled:opacity-40"
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
        <p class="text-secondary">Synced: {{ syncResult.syncedCountries.join(", ") || "none" }}</p>
        <p v-if="syncResult.failedCountries.length > 0" class="text-error mt-1">
          Failed: {{ syncResult.failedCountries.join(", ") }}
        </p>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onMounted, reactive, ref, watch } from "vue";

import {
  createPerson,
  deletePerson,
  getAllPeopleStats,
  syncBankHolidays,
  updatePerson,
} from "@/api/admin";
import { getColorsInUse } from "@/api/people";
import ColorPicker from "@/components/ColorPicker.vue";
import YearPicker from "@/components/YearPicker.vue";
import { useConfigStore } from "@/stores/config";
import { usePeopleStore } from "@/stores/people";
import type { SyncBankHolidaysResponse } from "@/types/bankHoliday";
import type { PersonAbsenceStatsDto, PersonDto } from "@/types/person";

const configStore = useConfigStore();
const regionCountries = computed(() => configStore.config?.regions ?? []);
const palette = computed(() => configStore.config?.colorPalette ?? []);
const configLoading = ref(true);

// This is the same store HomeView's CalendarGrid uses. Routing admin CRUD through it
// instead of a separate fetch keeps both views in sync with a single refresh() call.
const peopleStore = usePeopleStore();
const { people } = storeToRefs(peopleStore);
const loadingPeople = ref(true);
const colorsInUse = ref<string[]>([]);

const editingId = ref<number | null>(null);
const editingOriginalColor = ref<string | null>(null);
const deletingId = ref<number | null>(null);
const formError = ref<string | null>(null);
const submittingForm = ref(false);

const form = reactive({
  username: "",
  firstName: "",
  lastName: "",
  displayName: "",
  homeCity: null as string | null,
  color: "",
});

const canSubmitForm = computed(() => {
  if (!form.displayName.trim() || !form.color) return false;
  if (editingId.value) return true;
  return form.username.trim().length > 0;
});

async function loadPeople(forceRefresh = false) {
  loadingPeople.value = true;
  try {
    const [, { colors }] = await Promise.all([
      forceRefresh ? peopleStore.refresh() : peopleStore.loadOnce(),
      getColorsInUse(),
    ]);
    colorsInUse.value = colors;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load people:", error);
  } finally {
    loadingPeople.value = false;
  }
}

const statsYear = ref(new Date().getFullYear());
const statsByPerson = ref<Map<number, PersonAbsenceStatsDto>>(new Map());
const statsLoading = ref(true);

async function loadStats() {
  statsLoading.value = true;
  try {
    const { stats } = await getAllPeopleStats(statsYear.value);
    statsByPerson.value = new Map(stats.map((s) => [s.personId, s]));
  } catch (error: unknown) {
    console.error("ERROR => Failed to load people stats:", error);
  } finally {
    statsLoading.value = false;
  }
}

watch(statsYear, loadStats);

onMounted(() => {
  loadPeople();
  loadStats();
  configStore
    .loadOnce()
    .catch((error: unknown) => {
      console.error("ERROR => Failed to load app config:", error);
    })
    .finally(() => {
      configLoading.value = false;
    });
});

function resetForm() {
  form.username = "";
  form.firstName = "";
  form.lastName = "";
  form.displayName = "";
  form.homeCity = null;
  form.color = "";
}

function startEdit(person: PersonDto) {
  editingId.value = person.id;
  editingOriginalColor.value = person.color;
  formError.value = null;
  form.displayName = person.displayName ?? "";
  form.homeCity = person.homeCity;
  form.color = person.color;
}

function cancelEdit() {
  editingId.value = null;
  editingOriginalColor.value = null;
  formError.value = null;
  resetForm();
}

async function submitForm() {
  if (!canSubmitForm.value) return;

  formError.value = null;
  submittingForm.value = true;
  try {
    if (editingId.value) {
      await updatePerson(editingId.value, {
        displayName: form.displayName.trim(),
        homeCity: form.homeCity,
        color: form.color,
      });
    } else {
      await createPerson({
        username: form.username.trim(),
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        displayName: form.displayName.trim(),
        homeCity: form.homeCity,
        color: form.color,
      });
    }
    editingId.value = null;
    editingOriginalColor.value = null;
    resetForm();
    await loadPeople(true);
  } catch (error: unknown) {
    console.error("ERROR => Failed to save person:", error);
    formError.value = error instanceof Error ? error.message : "Failed to save person.";
  } finally {
    submittingForm.value = false;
  }
}

async function handleDelete(person: PersonDto) {
  if (!confirm(`Delete ${person.displayName ?? person.username}?`)) return;

  deletingId.value = person.id;
  try {
    await deletePerson(person.id);
    if (editingId.value === person.id) cancelEdit();
    await loadPeople(true);
  } catch (error: unknown) {
    console.error("ERROR => Failed to delete person:", error);
  } finally {
    deletingId.value = null;
  }
}

const syncYear = ref(new Date().getFullYear());
const syncing = ref(false);
const syncError = ref<string | null>(null);
const syncResult = ref<SyncBankHolidaysResponse | null>(null);

async function handleSync() {
  syncing.value = true;
  syncError.value = null;
  syncResult.value = null;
  try {
    syncResult.value = await syncBankHolidays(syncYear.value);
  } catch (error: unknown) {
    console.error("ERROR => Failed to sync bank holidays:", error);
    syncError.value = error instanceof Error ? error.message : "Failed to sync bank holidays.";
  } finally {
    syncing.value = false;
  }
}
</script>
