<template>
  <div class="max-w-3xl mx-auto py-8 flex flex-col gap-10">
    <section v-if="canManagePeople || canReadReports">
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

    <section v-if="canSyncHolidays">
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

    <section v-if="canApproveAbsences">
      <h4>Absence approvals</h4>
      <p class="text-sm text-secondary mt-1">
        Requests waiting for a decision, oldest first. Approving needs nothing further;
        rejecting can carry a note explaining why, which the requester sees.
      </p>

      <p v-if="pendingLoading && pendingAbsences.length === 0" class="text-sm text-secondary mt-3">
        Loading…
      </p>

      <ul v-else-if="pendingAbsences.length > 0" class="flex flex-col gap-3 mt-3">
        <li
          v-for="absence in pendingAbsences"
          :key="absence.id"
          class="border border-border rounded-8 p-3"
        >
          <div class="flex items-center justify-between gap-3 flex-wrap">
            <div class="text-sm">
              <span class="font-semibold">{{ personLabel(absence.personId) }}</span>
              <span class="text-secondary">
                — {{ absence.type }}, {{ absence.startDate }} → {{ absence.endDate }}</span
              >
            </div>
            <div class="flex items-center gap-3 shrink-0">
              <button
                type="button"
                class="text-xs font-semibold text-primary-text hover:text-accent transition-colors disabled:opacity-40"
                :disabled="reviewingId === absence.id"
                @click="approve(absence)"
              >
                Approve
              </button>
              <button
                type="button"
                class="text-xs font-semibold text-error hover:opacity-70 transition-opacity disabled:opacity-40"
                :disabled="reviewingId === absence.id"
                @click="toggleReject(absence.id)"
              >
                Reject
              </button>
            </div>
          </div>

          <p v-if="absence.note" class="text-sm text-secondary mt-2">{{ absence.note }}</p>

          <div v-if="rejectingId === absence.id" class="flex items-center gap-2 mt-3">
            <input
              v-model="rejectNote"
              type="text"
              maxlength="500"
              placeholder="Reason (optional)"
              class="flex-1 border border-border-strong rounded-8 px-3 py-1.5 text-sm"
            />
            <button
              type="button"
              class="text-xs font-semibold text-error hover:opacity-70 transition-opacity disabled:opacity-40"
              :disabled="reviewingId === absence.id"
              @click="reject(absence)"
            >
              Confirm
            </button>
            <button
              type="button"
              class="text-xs text-secondary hover:text-ink transition-colors"
              @click="rejectingId = null"
            >
              Cancel
            </button>
          </div>

          <p v-if="reviewError === absence.id" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mt-2">
            {{ reviewErrorMessage }}
          </p>
        </li>
      </ul>
      <p v-else class="text-sm text-secondary mt-3">Nothing waiting for review.</p>
    </section>

    <section v-if="canReadAudit">
      <h4>Audit log</h4>
      <p class="text-sm text-secondary mt-1">
        Every change to a profile, a manually added bank holiday, or a deleted absence, kept after
        the thing it happened to is gone. Notes are never recorded.
      </p>

      <p v-if="auditLoading && auditEntries.length === 0" class="text-sm text-secondary mt-3">
        Loading…
      </p>

      <table v-else-if="auditEntries.length > 0" class="w-full mt-3 text-sm">
        <thead>
          <tr class="text-left text-xs font-semibold text-secondary border-b border-border">
            <th class="py-2 pr-2 whitespace-nowrap">When</th>
            <th class="py-2 pr-2">Who</th>
            <th class="py-2 pr-2">Action</th>
            <th class="py-2 pr-2">What</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="entry in auditEntries" :key="entry.id" class="border-b border-border">
            <td class="py-2 pr-2 text-secondary whitespace-nowrap">
              {{ formatOccurredAt(entry.occurredAt) }}
            </td>
            <td class="py-2 pr-2">{{ entry.actor }}</td>
            <td class="py-2 pr-2 text-secondary">{{ entry.action }}</td>
            <td class="py-2 pr-2">
              {{ entry.target }}
              <span v-if="entry.details" class="text-secondary">— {{ entry.details }}</span>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-else class="text-sm text-secondary mt-3">Nothing has happened yet.</p>

      <div v-if="auditEntries.length < auditTotal" class="mt-4">
        <button
          type="button"
          class="text-sm text-secondary hover:text-ink transition-colors disabled:opacity-40"
          :disabled="auditLoading"
          @click="loadAudit(true)"
        >
          {{ auditLoading ? "Loading…" : `Show older (${auditTotal - auditEntries.length} more)` }}
        </button>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onMounted, reactive, ref, watch } from "vue";

import {
  approveAbsence,
  createPerson,
  deletePerson,
  getAllPeopleStats,
  getAuditLog,
  getPendingAbsences,
  rejectAbsence,
  syncBankHolidays,
  updatePerson,
} from "@/api/admin";
import { useAuthStore } from "@/auth";
import RegionPicker from "@/components/RegionPicker.vue";
import YearPicker from "@/components/YearPicker.vue";
import { groupRegions } from "@/lib/regionGroups";
import { usePeopleStore } from "@/stores/people";
import type { AbsenceDto } from "@/types/absence";
import type { AuditEntryDto } from "@/types/audit";
import type { SyncBankHolidaysResponse } from "@/types/bankHoliday";
import { Permission } from "@/types/permission";
import type { PersonAbsenceStatsDto, PersonDto } from "@/types/person";

// Each section stands on its own permission, so this page is whatever the signed-in user
// is allowed to see of it. The router only keeps out someone who would see none of it.
const authStore = useAuthStore();
const canManagePeople = computed(() => authStore.can(Permission.peopleManage));
const canReadReports = computed(() => authStore.can(Permission.reportsRead));
const canSyncHolidays = computed(() => authStore.can(Permission.holidaysSync));
const canReadAudit = computed(() => authStore.can(Permission.auditRead));
const canApproveAbsences = computed(() => authStore.can(Permission.absencesApprove));

// This is the same store HomeView's CalendarGrid uses. Routing admin CRUD through it
// instead of a separate fetch keeps both views in sync with a single refresh() call.
const peopleStore = usePeopleStore();
const { people, regionLabels } = storeToRefs(peopleStore);

// The regions worth offering a re-sync for are the ones the team occupies, which is
// exactly what the scheduled sync covers and what the API will accept here.
const regionGroups = computed(() => groupRegions(regionLabels.value));
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

const AUDIT_PAGE_SIZE = 50;
const auditEntries = ref<AuditEntryDto[]>([]);
const auditTotal = ref(0);
const auditLoading = ref(false);

// Paged by offset from the newest, so "show older" keeps walking back through one log
// rather than re-reading it. Anything written while the page is open shows up on a reload,
// which is soon enough for a log nobody watches live.
async function loadAudit(older = false) {
  auditLoading.value = true;
  try {
    const { entries, total } = await getAuditLog(
      AUDIT_PAGE_SIZE,
      older ? auditEntries.value.length : 0,
    );
    auditEntries.value = older ? [...auditEntries.value, ...entries] : entries;
    auditTotal.value = total;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load the audit log:", error);
  } finally {
    auditLoading.value = false;
  }
}

const occurredAtFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

// The API stores UTC and serializes it without an offset, which the browser would read as
// local time. Marked as UTC before parsing, so the log renders wherever the reader is.
function formatOccurredAt(occurredAt: string): string {
  const utc = /(Z|[+-]\d{2}:\d{2})$/.test(occurredAt) ? occurredAt : `${occurredAt}Z`;
  return occurredAtFormat.format(new Date(utc));
}

const pendingAbsences = ref<AbsenceDto[]>([]);
const pendingLoading = ref(false);
const reviewingId = ref<number | null>(null);
const rejectingId = ref<number | null>(null);
const rejectNote = ref("");
const reviewError = ref<number | null>(null);
const reviewErrorMessage = ref("");

async function loadPending() {
  pendingLoading.value = true;
  try {
    const { absences } = await getPendingAbsences();
    pendingAbsences.value = absences;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load pending absences:", error);
  } finally {
    pendingLoading.value = false;
  }
}

// people is keyed by id already loaded for the People section above, so this reuses it
// rather than the pending response carrying names of its own.
function personLabel(personId: number): string {
  const person = people.value.find((p) => p.id === personId);
  return person ? (person.displayName ?? person.username) : `#${personId}`;
}

function toggleReject(absenceId: number) {
  rejectingId.value = rejectingId.value === absenceId ? null : absenceId;
  rejectNote.value = "";
}

// approve() and reject() are the same shape (set the loading id, call the API, drop the
// absence from the queue on success, report the error on failure); this runs that shape
// once with whichever call and fallback message differs between the two.
async function review(
  absence: AbsenceDto,
  action: (id: number) => Promise<AbsenceDto>,
  fallbackMessage: string,
  onSuccess?: () => void,
) {
  reviewingId.value = absence.id;
  reviewError.value = null;
  try {
    await action(absence.id);
    pendingAbsences.value = pendingAbsences.value.filter((a) => a.id !== absence.id);
    onSuccess?.();
  } catch (error: unknown) {
    console.error("ERROR => Failed to review absence:", error);
    reviewError.value = absence.id;
    reviewErrorMessage.value = error instanceof Error ? error.message : fallbackMessage;
  } finally {
    reviewingId.value = null;
  }
}

function approve(absence: AbsenceDto) {
  return review(absence, approveAbsence, "Failed to approve absence.");
}

function reject(absence: AbsenceDto) {
  return review(
    absence,
    (id) => rejectAbsence(id, rejectNote.value.trim() || null),
    "Failed to reject absence.",
    () => {
      rejectingId.value = null;
    },
  );
}

onMounted(() => {
  loadPeople();
  if (canReadReports.value) loadStats();
  if (canReadAudit.value) loadAudit();
  // Not gated on absenceApprovalsEnabled: a request filed while approvals were on can
  // still be sitting there after they're turned back off, and this is the only place to
  // resolve it.
  if (canApproveAbsences.value) loadPending();
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
