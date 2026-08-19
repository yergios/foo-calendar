<template>
  <section>
    <div class="flex items-center justify-between">
      <h4>People</h4>
      <YearPicker v-if="canReadReports" v-model="statsYear">
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
          <th v-if="canReadReports" class="py-2 pr-2 text-center">Vacation</th>
          <th v-if="canReadReports" class="py-2 pr-2 text-center">Sick</th>
          <th v-if="canReadReports" class="py-2 pr-2 text-center">Other</th>
          <th class="py-2 pr-2"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="person in people" :key="person.id" class="border-b border-border">
          <td class="py-2 pr-2">{{ person.displayName ?? person.username }}</td>
          <td class="py-2 pr-2 text-secondary">{{ person.username }}</td>
          <td class="py-2 pr-2 text-secondary">{{ person.homeCity ?? "—" }}</td>
          <td v-if="canReadReports" class="py-2 pr-2 text-center text-secondary">
            {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.vacationDays ?? 0) }}
          </td>
          <td v-if="canReadReports" class="py-2 pr-2 text-center text-secondary">
            {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.sickDays ?? 0) }}
          </td>
          <td v-if="canReadReports" class="py-2 pr-2 text-center text-secondary">
            {{ statsLoading ? "…" : (statsByPerson.get(person.id)?.otherDays ?? 0) }}
          </td>
          <td v-if="canManagePeople" class="py-2 pr-2 text-right whitespace-nowrap">
            <button
              type="button"
              class="text-xs font-semibold text-primary-text hover:text-accent transition-colors"
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

    <div v-if="canManagePeople" class="mt-6 border border-border rounded-12 p-4">
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
            class="border border-border-strong rounded-8 px-3 py-2"
          />
        </div>
        <div v-if="!editingId" class="flex flex-col gap-1.5">
          <label class="text-xs font-semibold text-secondary">First name</label>
          <input
            v-model="form.firstName"
            type="text"
            maxlength="100"
            placeholder="e.g. Jane"
            class="border border-border-strong rounded-8 px-3 py-2"
          />
        </div>
        <div v-if="!editingId" class="flex flex-col gap-1.5">
          <label class="text-xs font-semibold text-secondary">Last name</label>
          <input
            v-model="form.lastName"
            type="text"
            maxlength="100"
            placeholder="e.g. Doe"
            class="border border-border-strong rounded-8 px-3 py-2"
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
            class="border border-border-strong rounded-8 px-3 py-2"
          />
        </div>
        <!-- Keyed so switching to another person, or back to the blank form, rebuilds
             the picker around their region instead of keeping the last one's. -->
        <RegionPicker :key="editingId ?? 'new'" v-model="form.homeCity" id-prefix="admin" />
      </div>

      <p v-if="formError" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mt-3">
        {{ formError }}
      </p>

      <div class="flex gap-3 mt-4">
        <button
          type="button"
          class="bg-primary text-on-primary border border-transparent rounded-8 px-4 py-2 text-sm transition-all duration-150 ease-out hover:bg-primary-hover disabled:opacity-40"
          :disabled="!canSubmitForm || submittingForm"
          @click="submitForm"
        >
          {{ submittingForm ? "Saving…" : editingId ? "Save changes" : "Add person" }}
        </button>
        <button
          v-if="editingId"
          type="button"
          class="text-sm text-secondary hover:text-ink transition-colors"
          @click="cancelEdit"
        >
          Cancel
        </button>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onMounted, reactive, ref, watch } from "vue";

import { createPerson, deletePerson, getAllPeopleStats, updatePerson } from "@/api/admin";
import { useAuthStore } from "@/auth";
import RegionPicker from "@/components/RegionPicker.vue";
import YearPicker from "@/components/YearPicker.vue";
import { useStaleGuard } from "@/composables/useStaleGuard";
import { usePeopleStore } from "@/stores/people";
import { Permission } from "@/types/permission";
import type { PersonAbsenceStatsDto, PersonDto } from "@/types/person";

// Reachable at all with either permission (see adminSections.ts); which pieces of the
// page that actually unlocks - the stats columns, the add/edit/delete form - is still
// decided permission by permission below.
const authStore = useAuthStore();
const canManagePeople = computed(() => authStore.can(Permission.peopleManage));
const canReadReports = computed(() => authStore.can(Permission.reportsRead));

// This is the same store HomeView's CalendarGrid uses. Routing admin CRUD through it
// instead of a separate fetch keeps both views in sync with a single refresh() call.
const peopleStore = usePeopleStore();
const { people } = storeToRefs(peopleStore);

const loadingPeople = ref(true);

const editingId = ref<number | null>(null);
const deletingId = ref<number | null>(null);
const formError = ref<string | null>(null);
const submittingForm = ref(false);

const form = reactive({
  username: "",
  firstName: "",
  lastName: "",
  displayName: "",
  homeCity: null as string | null,
});

const canSubmitForm = computed(() => {
  if (!form.displayName.trim()) return false;
  if (editingId.value) return true;
  return form.username.trim().length > 0;
});

async function loadPeople(forceRefresh = false) {
  loadingPeople.value = true;
  try {
    await (forceRefresh ? peopleStore.refresh() : peopleStore.loadOnce());
  } catch (error: unknown) {
    console.error("ERROR => Failed to load people:", error);
  } finally {
    loadingPeople.value = false;
  }
}

const statsYear = ref(new Date().getFullYear());
const statsByPerson = ref<Map<number, PersonAbsenceStatsDto>>(new Map());
const statsLoading = ref(true);

const isLatestStatsLoad = useStaleGuard();

async function loadStats() {
  const isCurrent = isLatestStatsLoad();
  statsLoading.value = true;
  try {
    const { stats } = await getAllPeopleStats(statsYear.value);
    if (!isCurrent()) return;
    statsByPerson.value = new Map(stats.map((s) => [s.personId, s]));
  } catch (error: unknown) {
    console.error("ERROR => Failed to load people stats:", error);
  } finally {
    if (isCurrent()) statsLoading.value = false;
  }
}

watch(statsYear, loadStats);

onMounted(() => {
  loadPeople();
  if (canReadReports.value) loadStats();
});

function resetForm() {
  form.username = "";
  form.firstName = "";
  form.lastName = "";
  form.displayName = "";
  form.homeCity = null;
}

function startEdit(person: PersonDto) {
  editingId.value = person.id;
  formError.value = null;
  form.displayName = person.displayName ?? "";
  form.homeCity = person.homeCity;
}

function cancelEdit() {
  editingId.value = null;
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
      });
    } else {
      await createPerson({
        username: form.username.trim(),
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        displayName: form.displayName.trim(),
        homeCity: form.homeCity,
      });
    }
    cancelEdit();
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
</script>
