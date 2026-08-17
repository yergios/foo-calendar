<template>
  <AppModal :title="modalTitle" @close="$emit('close')">
    <div v-if="view === 'list'" class="flex flex-col gap-2">
      <p class="text-sm text-secondary mb-1">
        {{ formatLongDate(props.dayAbsences[0].startDate) }}
      </p>

      <div
        v-for="a in props.dayAbsences"
        :key="a.id"
        role="button"
        tabindex="0"
        class="flex items-center justify-between gap-2 border border-border-strong rounded-8 px-3 py-2 hover:bg-accent/10 transition-colors text-left cursor-pointer"
        @click="openForEdit(a)"
        @keydown.enter="openForEdit(a)"
      >
        <span class="truncate text-sm">
          {{ formatTimeRange(a) }}<span v-if="a.note" class="text-secondary"> · {{ a.note }}</span>
        </span>
        <button
          type="button"
          class="text-error shrink-0 px-1.5 text-lg leading-none hover:opacity-70"
          title="Delete"
          @click.stop="deleteFromList(a)"
        >
          ×
        </button>
      </div>

      <button
        type="button"
        class="text-sm text-accent font-semibold text-left mt-1"
        @click="openForAdd"
      >
        + Add another for this day
      </button>

      <p v-if="error" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mt-1">
        {{ error }}
      </p>
    </div>

    <div v-else class="flex flex-col gap-4">
      <div class="flex flex-col gap-1.5">
        <label for="absenceType" class="text-xs font-semibold text-secondary"
          >Type<span class="text-error ml-0.5" title="Required">*</span></label
        >
        <select
          id="absenceType"
          v-model="type"
          class="border border-border-strong rounded-8 px-3 py-2 bg-background"
        >
          <option value="Vacation">Vacation</option>
          <option value="Appointment">Appointment</option>
          <option value="Sick">Sick</option>
          <option value="Other">Other</option>
        </select>
      </div>

      <label v-if="type === 'Other'" class="flex items-center gap-2 text-sm">
        <input v-model="otherIsTimeBased" type="checkbox" class="rounded" />
        Specific time instead of full days
      </label>

      <template v-if="!showTimeFields">
        <div class="flex flex-col gap-1.5">
          <label for="absenceStart" class="text-xs font-semibold text-secondary"
            >Start date<span class="text-error ml-0.5" title="Required">*</span></label
          >
          <input
            id="absenceStart"
            v-model="startDate"
            type="date"
            class="border border-border-strong rounded-8 px-3 py-2"
          />
        </div>

        <div class="flex flex-col gap-1.5">
          <label for="absenceEnd" class="text-xs font-semibold text-secondary"
            >End date<span class="text-error ml-0.5" title="Required">*</span></label
          >
          <input
            id="absenceEnd"
            v-model="endDate"
            type="date"
            class="border border-border-strong rounded-8 px-3 py-2"
          />
        </div>
      </template>

      <template v-else>
        <div class="flex flex-col gap-1.5">
          <label for="apptDate" class="text-xs font-semibold text-secondary"
            >When?<span class="text-error ml-0.5" title="Required">*</span></label
          >
          <input
            id="apptDate"
            v-model="apptDate"
            type="date"
            class="border border-border-strong rounded-8 px-3 py-2"
          />
          <p class="text-xs text-secondary">{{ formatLongDate(apptDate) }}</p>
        </div>

        <div class="flex gap-3">
          <div class="flex flex-col gap-1.5 flex-1">
            <label for="apptStart" class="text-xs font-semibold text-secondary">
              Start time<span class="text-error ml-0.5" title="Required">*</span>
              <span class="font-normal text-secondary/70">
                ({{ configStore.config?.homeTimeZone }})</span
              >
            </label>
            <input
              id="apptStart"
              v-model="apptStartTime"
              type="time"
              class="border border-border-strong rounded-8 px-3 py-2"
            />
          </div>

          <div class="flex flex-col gap-1.5 flex-1">
            <label for="apptEnd" class="text-xs font-semibold text-secondary">
              End time<span class="text-error ml-0.5" title="Required">*</span>
              <span class="font-normal text-secondary/70">
                ({{ configStore.config?.homeTimeZone }})</span
              >
            </label>
            <input
              id="apptEnd"
              v-model="apptEndTime"
              type="time"
              class="border border-border-strong rounded-8 px-3 py-2"
            />
          </div>
        </div>
      </template>

      <div class="flex flex-col gap-1.5">
        <label for="absenceNote" class="text-xs font-semibold text-secondary">Note</label>
        <textarea
          id="absenceNote"
          v-model="note"
          rows="3"
          maxlength="500"
          placeholder="e.g. Traveling to see family, back to full availability the following Monday."
          class="border border-border-strong rounded-8 px-3 py-2 resize-y w-full"
        />
      </div>
    </div>

    <template v-if="view === 'form'" #footer>
      <p
        v-if="resubmitsForApproval"
        class="text-sm text-secondary bg-surface rounded-8 px-3 py-2 mb-3"
      >
        This was already approved. Saving changes sends it back for approval.
      </p>
      <p v-if="error" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2 mb-3">
        {{ error }}
      </p>
      <div class="flex items-center gap-3">
        <button
          type="button"
          class="flex-1 bg-primary text-on-primary border border-transparent rounded-8 py-2.5 transition-all duration-150 ease-out hover:bg-primary-hover hover:shadow-card disabled:opacity-40"
          :disabled="!canSubmit || submitting || deleting"
          :title="
            canSubmit
              ? undefined
              : showTimeFields
                ? 'Set a day and a valid time range first.'
                : 'Set a valid start and end date first.'
          "
          @click="submit"
        >
          {{ submitting ? "Saving…" : isEditing ? "Save changes" : "Add absence" }}
        </button>
        <button
          v-if="isEditing"
          type="button"
          class="flex-1 text-error border border-error rounded-8 py-2.5 hover:bg-error-bg transition-all duration-150 ease-out disabled:opacity-40"
          :disabled="submitting || deleting"
          @click="handleDelete"
        >
          {{ deleting ? "Deleting…" : "Delete" }}
        </button>
      </div>
    </template>
  </AppModal>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";

import { createAbsence, deleteAbsence, updateAbsence } from "@/api/absences";
import AppModal from "@/components/AppModal.vue";
import { formatLongDate, toApiTime, today } from "@/lib/dates";
import { useConfigStore } from "@/stores/config";
import type { AbsenceDto, AbsenceType } from "@/types/absence";

const props = withDefaults(
  defineProps<{
    initialDate?: string | null;
    /** The viewer's own absences already on the clicked day, in list order. Decides which
     * sub-view the modal opens into: none -> straight to the add form; one -> straight to
     * the edit form for it, whatever its type; two or more (only possible when several
     * time-based absences share a day, see CalendarGrid.vue's mutual-exclusivity note) ->
     * the list below. */
    dayAbsences?: AbsenceDto[];
  }>(),
  {
    initialDate: null,
    dayAbsences: () => [],
  },
);

const emit = defineEmits<{
  close: [];
  saved: [absence: AbsenceDto];
  deleted: [id: number];
}>();

const configStore = useConfigStore();

// Callers already tend to load this on mount too, but nothing guarantees this modal isn't
// the first thing on screen to need it, so it fetches its own copy. loadOnce() dedupes
// against any already-in-flight or cached request, so this never costs a second round trip.
onMounted(() => {
  configStore.loadOnce().catch((error: unknown) => {
    console.error("ERROR => Failed to load app config:", error);
  });
});

type ModalView = "list" | "form";

// Starts in "list" only when there's genuinely a choice to make; none or exactly one
// absence skips straight to the one meaningful view.
const view = ref<ModalView>(props.dayAbsences.length >= 2 ? "list" : "form");

// The absence currently being edited by the form, or null while adding. Can change during
// the modal's lifetime (a list row click, or "add another"), unlike the old singular
// `absence` prop this replaces.
const editingAbsence = ref<AbsenceDto | null>(
  props.dayAbsences.length === 1 ? props.dayAbsences[0] : null,
);
const isEditing = computed(() => editingAbsence.value !== null);

const modalTitle = computed(() => {
  if (view.value === "list") return "Choose an absence";
  return isEditing.value ? "Edit absence" : "Add absence";
});

const resubmitsForApproval = computed(
  () =>
    isEditing.value &&
    editingAbsence.value?.status === "Approved" &&
    configStore.config?.absenceApprovalsEnabled === true,
);

const startDate = ref("");
const endDate = ref("");
const type = ref<AbsenceType>("Vacation");
// Only meaningful while type is "Other": Appointment is always time-based and every other
// type is never time-based, so neither needs this to decide anything.
const otherIsTimeBased = ref(false);
const note = ref("");
const apptDate = ref("");
const apptStartTime = ref("");
const apptEndTime = ref("");
const error = ref<string | null>(null);
const submitting = ref(false);
const deleting = ref(false);

// Whether the day/time fields (rather than the start/end date fields) are the ones in
// play right now: always for Appointment, and for Other only once the checkbox is on.
const showTimeFields = computed(
  () => type.value === "Appointment" || (type.value === "Other" && otherIsTimeBased.value),
);

// Re-seeds every form field from scratch, since editingAbsence can now change mid-lifecycle
// (openForEdit/openForAdd below) instead of being fixed for the modal's whole lifetime.
function seedFormFrom(absence: AbsenceDto | null) {
  const day = absence?.startDate ?? props.initialDate ?? today();
  startDate.value = absence?.startDate ?? day;
  endDate.value = absence?.endDate ?? day;
  type.value = absence?.type ?? "Vacation";
  otherIsTimeBased.value = absence?.type === "Other" && !!absence.startTime;
  note.value = absence?.note ?? "";
  apptDate.value = absence?.startDate ?? day;
  apptStartTime.value = absence?.startTime?.slice(0, 5) ?? "";
  apptEndTime.value = absence?.endTime?.slice(0, 5) ?? "";
  error.value = null;
}

seedFormFrom(editingAbsence.value);

function openForEdit(absence: AbsenceDto) {
  editingAbsence.value = absence;
  seedFormFrom(absence);
  view.value = "form";
}

function openForAdd() {
  editingAbsence.value = null;
  seedFormFrom(null);
  view.value = "form";
}

function formatTimeRange(absence: AbsenceDto): string {
  return `${absence.startTime?.slice(0, 5) ?? ""}–${absence.endTime?.slice(0, 5) ?? ""}`;
}

// Pushing start past the current end date drags end along with it. Otherwise you'd
// end up with an invalid (end < start) range you'd have to notice and fix yourself.
watch(startDate, (newStart) => {
  if (endDate.value < newStart) endDate.value = newStart;
});

const canSubmit = computed(() => {
  if (showTimeFields.value) {
    return (
      !!apptDate.value &&
      !!apptStartTime.value &&
      !!apptEndTime.value &&
      apptEndTime.value > apptStartTime.value
    );
  }
  return !!startDate.value && !!endDate.value && endDate.value >= startDate.value;
});

async function submit() {
  if (!canSubmit.value) return;

  error.value = null;
  submitting.value = true;
  try {
    const payload = showTimeFields.value
      ? {
          startDate: apptDate.value,
          endDate: apptDate.value,
          type: type.value,
          note: note.value.trim() || null,
          startTime: toApiTime(apptStartTime.value),
          endTime: toApiTime(apptEndTime.value),
        }
      : {
          startDate: startDate.value,
          endDate: endDate.value,
          type: type.value,
          note: note.value.trim() || null,
          startTime: null,
          endTime: null,
        };
    const saved = isEditing.value
      ? await updateAbsence(editingAbsence.value!.id, payload)
      : await createAbsence(payload);
    emit("saved", saved);
  } catch (err: unknown) {
    console.error("ERROR => Failed to save absence:", err);
    error.value = err instanceof Error ? err.message : "Failed to save absence. Please try again.";
  } finally {
    submitting.value = false;
  }
}

// No confirm() here: opening the edit modal for a specific absence is already a
// deliberate act, so a second "are you sure" would just be extra friction.
async function handleDelete() {
  if (!editingAbsence.value) return;

  error.value = null;
  deleting.value = true;
  try {
    await deleteAbsence(editingAbsence.value.id);
    emit("deleted", editingAbsence.value.id);
  } catch (err: unknown) {
    console.error("ERROR => Failed to delete absence:", err);
    error.value =
      err instanceof Error ? err.message : "Failed to delete absence. Please try again.";
  } finally {
    deleting.value = false;
  }
}

// Closes the whole modal on success, same as the form's own delete above, rather than
// staying open with just that row removed: simpler, and still one click to "quickly
// delete" any one of a day's time-based absences.
async function deleteFromList(absence: AbsenceDto) {
  error.value = null;
  try {
    await deleteAbsence(absence.id);
    emit("deleted", absence.id);
  } catch (err: unknown) {
    console.error("ERROR => Failed to delete absence:", err);
    error.value =
      err instanceof Error ? err.message : "Failed to delete absence. Please try again.";
  }
}
</script>
