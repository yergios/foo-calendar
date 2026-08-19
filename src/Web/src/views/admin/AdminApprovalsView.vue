<template>
  <section>
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
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { onMounted, ref } from "vue";

import { approveAbsence, getPendingAbsences, rejectAbsence } from "@/api/admin";
import { usePeopleStore } from "@/stores/people";
import type { AbsenceDto } from "@/types/absence";

// Only for personLabel() below - the people list itself lives on the People page.
const peopleStore = usePeopleStore();
const { people } = storeToRefs(peopleStore);

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

// Falls back to the id when the people list hasn't loaded yet (or never will, for
// someone without PeopleManage/ReportsRead) rather than waiting on it to show anything.
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
  peopleStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load people:", error);
  });
  // Not gated on absenceApprovalsEnabled: a request filed while approvals were on can
  // still be sitting there after they're turned back off, and this is the only place to
  // resolve it.
  loadPending();
});
</script>
