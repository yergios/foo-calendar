<template>
  <div class="flex flex-col gap-5">
    <div class="flex flex-col gap-1.5">
      <label for="displayName" class="text-xs font-semibold text-secondary"
        >Display name<span class="text-error ml-0.5" title="Required">*</span></label
      >
      <input
        id="displayName"
        v-model="displayName"
        type="text"
        maxlength="100"
        placeholder="e.g. David R."
        class="border border-border-strong rounded-8 px-3 py-2"
      />
    </div>

    <RegionPicker v-model="homeCity" />

    <div v-if="configStore.config?.emailEnabled === true" class="flex flex-col gap-2.5">
      <label class="text-xs font-semibold text-secondary">Email notifications</label>

      <label class="flex items-center gap-2 text-sm">
        <input v-model="emailOnAbsenceReviewed" type="checkbox" />
        Email me when my absence request is approved or rejected
      </label>

      <label v-if="canApproveAbsences" class="flex items-center gap-2 text-sm">
        <input v-model="emailOnNewAbsenceRequest" type="checkbox" />
        Email me when a new absence request needs review
      </label>

      <label class="flex items-center gap-2 text-sm">
        <input v-model="emailOnWeeklyDigest" type="checkbox" />
        Email me a weekly digest of who's off
      </label>
    </div>

    <p v-if="error" class="text-sm text-error bg-error-bg rounded-8 px-3 py-2">{{ error }}</p>

    <button
      type="button"
      class="bg-primary text-on-primary border border-transparent rounded-8 py-2.5 transition-all duration-150 ease-out hover:bg-primary-hover hover:shadow-card active:scale-98 disabled:opacity-40 disabled:hover:bg-primary disabled:hover:shadow-none"
      :title="canSubmit ? undefined : 'Set a display name first.'"
      :disabled="!canSubmit || submitting"
      @click="submit"
    >
      {{ submitting ? "Saving…" : submitLabel }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";

import { useAuthStore } from "@/auth";
import RegionPicker from "@/components/RegionPicker.vue";
import { useConfigStore } from "@/stores/config";
import { Permission } from "@/types/permission";

const props = withDefaults(
  defineProps<{
    initialDisplayName?: string | null;
    initialHomeCity?: string | null;
    initialEmailOnAbsenceReviewed?: boolean;
    initialEmailOnNewAbsenceRequest?: boolean;
    initialEmailOnWeeklyDigest?: boolean;
    submitLabel?: string;
  }>(),
  {
    initialDisplayName: null,
    initialHomeCity: null,
    initialEmailOnAbsenceReviewed: false,
    initialEmailOnNewAbsenceRequest: false,
    initialEmailOnWeeklyDigest: false,
    submitLabel: "Save",
  },
);

const emit = defineEmits<{
  submit: [
    payload: {
      displayName: string | null;
      homeCity: string | null;
      emailOnAbsenceReviewed: boolean;
      emailOnNewAbsenceRequest: boolean;
      emailOnWeeklyDigest: boolean;
    },
    done: (error?: string | null) => void,
  ];
}>();

const authStore = useAuthStore();
const configStore = useConfigStore();

const displayName = ref(props.initialDisplayName ?? authStore.getFullName() ?? "");
const error = ref<string | null>(null);
const submitting = ref(false);

const homeCity = ref<string | null>(props.initialHomeCity);

const emailOnAbsenceReviewed = ref(props.initialEmailOnAbsenceReviewed);
const emailOnNewAbsenceRequest = ref(props.initialEmailOnNewAbsenceRequest);
const emailOnWeeklyDigest = ref(props.initialEmailOnWeeklyDigest);
const canApproveAbsences = computed(() => authStore.can(Permission.absencesApprove));

onMounted(() => {
  configStore
    .loadOnce()
    .catch((error: unknown) => console.error("ERROR => Failed to load profile form config:", error));
});

const canSubmit = computed(() => displayName.value.trim().length > 0);

function submit() {
  if (!canSubmit.value) return;
  error.value = null;
  submitting.value = true;
  emit(
    "submit",
    {
      displayName: displayName.value.trim() || null,
      homeCity: homeCity.value,
      emailOnAbsenceReviewed: emailOnAbsenceReviewed.value,
      emailOnNewAbsenceRequest: emailOnNewAbsenceRequest.value,
      emailOnWeeklyDigest: emailOnWeeklyDigest.value,
    },
    (failure?: string | null) => {
      submitting.value = false;
      error.value = failure ?? null;
    },
  );
}
</script>
