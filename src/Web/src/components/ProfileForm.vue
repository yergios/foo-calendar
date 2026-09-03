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
import { computed, ref } from "vue";

import { useAuthStore } from "@/auth";
import RegionPicker from "@/components/RegionPicker.vue";
import type { UpdatePersonRequest } from "@/types/person";

const props = withDefaults(
  defineProps<{
    initialDisplayName?: string | null;
    initialHomeCity?: string | null;
    submitLabel?: string;
  }>(),
  {
    initialDisplayName: null,
    initialHomeCity: null,
    submitLabel: "Save",
  },
);

const emit = defineEmits<{
  submit: [payload: UpdatePersonRequest, done: (error?: string | null) => void];
}>();

const authStore = useAuthStore();

const displayName = ref(props.initialDisplayName ?? authStore.getFullName() ?? "");
const error = ref<string | null>(null);
const submitting = ref(false);

const homeCity = ref<string | null>(props.initialHomeCity);

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
    },
    (failure?: string | null) => {
      submitting.value = false;
      error.value = failure ?? null;
    },
  );
}
</script>
