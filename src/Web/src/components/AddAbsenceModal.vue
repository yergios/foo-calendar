<template>
  <AppModal :title="isEditing ? 'Edit absence' : 'Add absence'" @close="$emit('close')">
    <div class="flex flex-col gap-4">
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
          <option value="Sick">Sick</option>
          <option value="Other">Other</option>
        </select>
      </div>

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

    <template #footer>
      <p v-if="resubmitsForApproval" class="text-sm text-secondary bg-surface rounded-8 px-3 py-2 mb-3">
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
          :title="canSubmit ? undefined : 'Set a valid start and end date first.'"
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
import { computed, ref, watch } from "vue";

import { createAbsence, deleteAbsence, updateAbsence } from "@/api/absences";
import AppModal from "@/components/AppModal.vue";
import { today } from "@/lib/dates";
import { useConfigStore } from "@/stores/config";
import type { AbsenceDto, AbsenceType } from "@/types/absence";

const props = withDefaults(
  defineProps<{
    initialDate?: string | null;
    absence?: AbsenceDto | null;
  }>(),
  {
    initialDate: null,
    absence: null,
  },
);

const emit = defineEmits<{
  close: [];
  saved: [absence: AbsenceDto];
  deleted: [id: number];
}>();

const isEditing = computed(() => props.absence !== null);

const configStore = useConfigStore();
const resubmitsForApproval = computed(
  () =>
    isEditing.value &&
    props.absence?.status === "Approved" &&
    configStore.config?.absenceApprovalsEnabled === true,
);

const startDate = ref(props.absence?.startDate ?? props.initialDate ?? today());
const endDate = ref(props.absence?.endDate ?? props.initialDate ?? today());
const type = ref<AbsenceType>(props.absence?.type ?? "Vacation");
const note = ref(props.absence?.note ?? "");
const error = ref<string | null>(null);
const submitting = ref(false);
const deleting = ref(false);

// Pushing start past the current end date drags end along with it. Otherwise you'd
// end up with an invalid (end < start) range you'd have to notice and fix yourself.
watch(startDate, (newStart) => {
  if (endDate.value < newStart) endDate.value = newStart;
});

const canSubmit = computed(
  () => !!startDate.value && !!endDate.value && endDate.value >= startDate.value,
);

async function submit() {
  if (!canSubmit.value) return;

  error.value = null;
  submitting.value = true;
  try {
    const payload = {
      startDate: startDate.value,
      endDate: endDate.value,
      type: type.value,
      note: note.value.trim() || null,
    };
    const saved = isEditing.value
      ? await updateAbsence(props.absence!.id, payload)
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
  if (!props.absence) return;

  error.value = null;
  deleting.value = true;
  try {
    await deleteAbsence(props.absence.id);
    emit("deleted", props.absence.id);
  } catch (err: unknown) {
    console.error("ERROR => Failed to delete absence:", err);
    error.value =
      err instanceof Error ? err.message : "Failed to delete absence. Please try again.";
  } finally {
    deleting.value = false;
  }
}
</script>
