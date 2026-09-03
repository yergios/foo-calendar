<template>
  <div class="flex flex-col gap-5">
    <!-- Only where there is no identity provider to say who you are, and only while
         setting a profile up: this is the name the API resolves every later request as,
         so it is fixed once claimed. Claiming it is part of filling this form in, which
         is why the deployment needs no sign-in screen of its own. -->
    <div v-if="asksForUsername" class="flex flex-col gap-1.5">
      <label for="username" class="text-xs font-semibold text-secondary"
        >Username<span class="text-error ml-0.5" title="Required">*</span></label
      >
      <input
        id="username"
        v-model="username"
        type="text"
        maxlength="100"
        autocomplete="username"
        placeholder="e.g. drossi"
        class="border border-border-strong rounded-8 px-3 py-2"
      />
      <p class="text-xs text-secondary">
        There is no sign-in on this calendar. Use the same username next time to come back to this
        profile.
      </p>
    </div>

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
      <div class="flex flex-col gap-1.5">
        <label for="email" class="text-xs font-semibold text-secondary">Email address</label>
        <input
          id="email"
          v-model="email"
          type="email"
          maxlength="255"
          autocomplete="email"
          placeholder="you@example.com"
          class="border border-border-strong rounded-8 px-3 py-2"
        />
        <p class="text-xs text-secondary">
          Where the notifications below go. Leave it empty to receive none.
        </p>
      </div>

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

import { useAuthStore, usesSelfAssertedIdentity } from "@/auth";
import RegionPicker from "@/components/RegionPicker.vue";
import { useConfigStore } from "@/stores/config";
import { Permission } from "@/types/permission";
import type { OnboardingRequest } from "@/types/person";

const props = withDefaults(
  defineProps<{
    initialDisplayName?: string | null;
    initialHomeCity?: string | null;
    initialEmail?: string | null;
    /** Setting a profile up rather than editing one, which is the only time a username
     * can still be chosen. */
    isOnboarding?: boolean;
    initialEmailOnAbsenceReviewed?: boolean;
    initialEmailOnNewAbsenceRequest?: boolean;
    initialEmailOnWeeklyDigest?: boolean;
    submitLabel?: string;
  }>(),
  {
    initialDisplayName: null,
    initialHomeCity: null,
    initialEmail: null,
    isOnboarding: false,
    initialEmailOnAbsenceReviewed: false,
    initialEmailOnNewAbsenceRequest: false,
    initialEmailOnWeeklyDigest: false,
    submitLabel: "Save",
  },
);

const emit = defineEmits<{
  submit: [payload: OnboardingRequest, done: (error?: string | null) => void];
}>();

const authStore = useAuthStore();
const configStore = useConfigStore();

const asksForUsername = computed(() => props.isOnboarding && usesSelfAssertedIdentity());

const displayName = ref(props.initialDisplayName ?? authStore.getFullName() ?? "");
const error = ref<string | null>(null);
const submitting = ref(false);

const homeCity = ref<string | null>(props.initialHomeCity);
const username = ref("");

// Prefilled from the identity provider's email claim when there is one, so the common case
// is leaving it alone. Editable regardless: the claim can be missing or wrong, and on a
// deployment with no identity provider there is no claim at all.
const email = ref(props.initialEmail ?? authStore.getEmail() ?? "");

const emailOnAbsenceReviewed = ref(props.initialEmailOnAbsenceReviewed);
const emailOnNewAbsenceRequest = ref(props.initialEmailOnNewAbsenceRequest);
const emailOnWeeklyDigest = ref(props.initialEmailOnWeeklyDigest);
const canApproveAbsences = computed(() => authStore.can(Permission.absencesApprove));

onMounted(() => {
  configStore
    .loadOnce()
    .catch((error: unknown) =>
      console.error("ERROR => Failed to load profile form config:", error),
    );
});

const canSubmit = computed(
  () =>
    displayName.value.trim().length > 0 &&
    (!asksForUsername.value || username.value.trim().length > 0),
);

function submit() {
  if (!canSubmit.value) return;
  error.value = null;
  submitting.value = true;
  emit(
    "submit",
    {
      username: asksForUsername.value ? username.value.trim() : null,
      displayName: displayName.value.trim() || null,
      homeCity: homeCity.value,
      // Empty string, not null: null tells the API to leave the stored address alone,
      // which is what a caller that doesn't edit addresses sends. This form does edit it,
      // so an emptied box has to mean "clear it".
      email: email.value.trim(),
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
