<template>
  <AppModal title="Welcome" size="md" @close="$emit('close')">
    <p class="text-sm text-secondary -mt-1 mb-4">
      Set up your profile to start using the calendar.
    </p>
    <ProfileForm
      is-onboarding
      submit-label="Get started"
      @submit="(payload, done) => $emit('submit', payload, done)"
    />
  </AppModal>
</template>

<script setup lang="ts">
import AppModal from "@/components/AppModal.vue";
import type { OnboardingRequest } from "@/types/person";

import ProfileForm from "./ProfileForm.vue";

defineEmits<{
  submit: [payload: OnboardingRequest, done: (error?: string | null) => void];
  close: [];
}>();
</script>
