<template>
  <AppModal :title="isEditing ? 'Edit bank holiday' : 'Add bank holiday'" @close="$emit('close')">
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-1.5">
        <label for="holidayDate" class="text-xs font-semibold text-secondary"
          >Date<span class="text-error ml-0.5" data-tooltip="Required">*</span></label
        >
        <input
          id="holidayDate"
          v-model="date"
          type="date"
          class="border border-border-strong rounded-8 px-3 py-2"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <label for="holidayRegion" class="text-xs font-semibold text-secondary"
          >Region<span class="text-error ml-0.5" data-tooltip="Required">*</span></label
        >
        <select
          id="holidayRegion"
          v-model="region"
          class="border border-border-strong rounded-8 px-3 py-2 bg-background"
        >
          <option value="" disabled>Select a region</option>
          <optgroup v-for="country in regionGroups" :key="country.code" :label="country.label">
            <option :value="country.code">{{ country.label }} (nationwide)</option>
            <option v-for="opt in country.regions" :key="opt.code" :value="opt.code">
              {{ opt.label }}
            </option>
          </optgroup>
        </select>
      </div>

      <div class="flex flex-col gap-1.5">
        <label for="holidayName" class="text-xs font-semibold text-secondary"
          >Name<span class="text-error ml-0.5" data-tooltip="Required">*</span></label
        >
        <input
          id="holidayName"
          v-model="name"
          type="text"
          maxlength="200"
          placeholder="e.g. Founder's Day"
          class="border border-border-strong rounded-8 px-3 py-2"
        />
      </div>

      <label class="flex items-center gap-2 text-sm text-secondary">
        <input v-model="isRecurringAnnually" type="checkbox" class="rounded-4" />
        Repeats every year
      </label>
    </div>

    <template #footer>
      <AppAlert v-if="error" class="mb-3">
        {{ error }}
      </AppAlert>
      <div class="flex items-center gap-3">
        <!-- Wrapper carries the tooltip: a disabled button fires no pointer events, so the
             explanation of why it's disabled would never be readable on the button itself. -->
        <span
          class="flex-1 flex"
          :data-tooltip="canSubmit ? undefined : 'Select a region, date, and name first.'"
        >
          <button
            type="button"
            class="w-full bg-primary text-on-primary border border-transparent rounded-8 py-2.5 transition-all hover:bg-primary-hover hover:shadow-raised disabled:opacity-40 disabled:hover:bg-primary disabled:hover:shadow-none"
            :disabled="!canSubmit || submitting || deleting"
            @click="submit"
          >
            {{ submitting ? "Saving…" : isEditing ? "Save changes" : "Add bank holiday" }}
          </button>
        </span>
        <button
          v-if="isEditing"
          type="button"
          class="flex-1 text-error border border-error rounded-8 py-2.5 hover:bg-error/10 transition-colors disabled:opacity-40 disabled:hover:bg-background"
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
import { storeToRefs } from "pinia";
import { computed, onMounted, ref } from "vue";

import { addManualHoliday, deleteBankHoliday, updateBankHoliday } from "@/api/bankHolidays";
import AppAlert from "@/components/AppAlert.vue";
import AppModal from "@/components/AppModal.vue";
import { groupRegions } from "@/lib/regionGroups";
import { usePeopleStore } from "@/stores/people";
import type { BankHolidayDto } from "@/types/bankHoliday";

const props = withDefaults(
  defineProps<{
    holiday?: BankHolidayDto | null;
    initialDate?: string | null;
  }>(),
  {
    holiday: null,
    initialDate: null,
  },
);

const emit = defineEmits<{
  close: [];
  saved: [holiday: BankHolidayDto];
  deleted: [id: number];
}>();

const peopleStore = usePeopleStore();
const { regionLabels } = storeToRefs(peopleStore);

// Only where the team already is: the API rejects anything else, since a holiday stored
// against a region nobody occupies would never appear on the calendar.
const regionGroups = computed(() => groupRegions(regionLabels.value));

const isEditing = computed(() => props.holiday !== null);

const region = ref(props.holiday?.region ?? "");
const date = ref(props.holiday?.date ?? props.initialDate ?? "");
const name = ref(props.holiday?.name ?? "");
const isRecurringAnnually = ref(props.holiday?.isRecurringAnnually ?? false);
const error = ref<string | null>(null);
const submitting = ref(false);
const deleting = ref(false);

onMounted(() => {
  peopleStore.loadOnce().catch((err: unknown) => {
    console.error("ERROR => Failed to load the team's regions:", err);
  });
});

const canSubmit = computed(() => !!region.value && !!date.value && name.value.trim().length > 0);

async function submit() {
  if (!canSubmit.value) return;

  error.value = null;
  submitting.value = true;
  try {
    const payload = {
      region: region.value,
      date: date.value,
      name: name.value.trim(),
      isRecurringAnnually: isRecurringAnnually.value,
    };
    const saved = isEditing.value
      ? await updateBankHoliday(props.holiday!.id, payload)
      : await addManualHoliday(payload);
    emit("saved", saved);
  } catch (err: unknown) {
    console.error("ERROR => Failed to save bank holiday:", err);
    error.value = "Failed to save bank holiday. It may already exist for that region and date.";
  } finally {
    submitting.value = false;
  }
}

// No confirm() here: opening the edit modal for a specific holiday is already a
// deliberate act, so a second "are you sure" would just be extra friction.
async function handleDelete() {
  if (!props.holiday) return;

  error.value = null;
  deleting.value = true;
  try {
    await deleteBankHoliday(props.holiday.id);
    emit("deleted", props.holiday.id);
  } catch (err: unknown) {
    console.error("ERROR => Failed to delete bank holiday:", err);
    error.value =
      err instanceof Error ? err.message : "Failed to delete bank holiday. Please try again.";
  } finally {
    deleting.value = false;
  }
}
</script>
