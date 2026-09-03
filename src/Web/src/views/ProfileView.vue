<template>
  <div class="mx-auto">
    <h4 class="text-center">Your profile</h4>

    <div v-if="initialLoading" class="flex gap-12 -w-xlg mx-auto py-8">
      <div class="flex flex-col gap-5 w-full max-w-md">
        <AppSkeleton class="h-4 w-24" />
        <AppSkeleton class="h-10 w-full" />
        <AppSkeleton class="h-4 w-16" />
        <AppSkeleton class="h-10 w-full" />
        <AppSkeleton class="h-24 w-full" />
        <AppSkeleton class="h-10 w-full" />
      </div>
      <div class="flex-1">
        <AppSkeleton class="h-6 w-40" />
        <div class="grid grid-cols-3 gap-3 mt-3">
          <AppSkeleton class="h-20" />
          <AppSkeleton class="h-20" />
          <AppSkeleton class="h-20" />
        </div>
      </div>
    </div>

    <div v-else-if="person" class="flex gap-12 -w-xlg mx-auto py-8">
      <div>
        <ProfileForm
          :initial-display-name="person.displayName"
          :initial-home-city="person.homeCity"
          submit-label="Save changes"
          @submit="handleSave"
        />
        <button
          type="button"
          class="mt-5 w-full text-error border border-error rounded-8 px-3 py-2.5 hover:bg-error-bg transition-all duration-150 ease-out disabled:opacity-40"
          :disabled="deleting"
          @click="handleDelete"
        >
          {{ deleting ? "Deleting…" : "Delete profile" }}
        </button>
        <SuccessMessage v-if="saved" class="text-sm mt-2">Profile saved.</SuccessMessage>
        <p v-if="deleteError" class="text-sm text-error mt-2">{{ deleteError }}</p>
      </div>
      <div>
        <YearPicker v-model="statsYear" class="justify-center">
          <h5>Absence stats {{ statsYear }}</h5>
        </YearPicker>
        <div v-if="statsLoading" class="grid grid-cols-2 gap-3 mt-3">
          <AppSkeleton class="h-20" />
          <AppSkeleton class="h-20" />
        </div>
        <div v-else-if="stats" class="grid grid-cols-2 gap-3 mt-3">
          <div class="bg-surface rounded-12 p-4 text-center">
            <div class="text-2xl font-bold text-ink">{{ stats.vacationDays }}</div>
            <div class="text-xs text-secondary mt-1">Vacation</div>
          </div>
          <div class="bg-surface rounded-12 p-4 text-center">
            <div class="text-2xl font-bold text-ink">{{ stats.otherDays }}</div>
            <div class="text-xs text-secondary mt-1">Other</div>
          </div>
        </div>
      </div>
    </div>

    <div v-else class="max-w-md mx-auto py-12 text-center flex flex-col items-center gap-3">
      <p class="text-sm text-secondary">You don't have a profile yet.</p>
      <button
        type="button"
        class="border border-accent text-accent rounded-pill px-3 py-1.5 text-sm font-semibold hover:bg-accent/10 transition-colors"
        @click="personStore.ensurePerson()"
      >
        Set up profile
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { onMounted, ref, watch } from "vue";

import { deleteMe, getMyStats, updateMe } from "@/api/people";
import AppSkeleton from "@/components/AppSkeleton.vue";
import ProfileForm from "@/components/ProfileForm.vue";
import SuccessMessage from "@/components/SuccessMessage.vue";
import YearPicker from "@/components/YearPicker.vue";
import { useStaleGuard } from "@/composables/useStaleGuard";
import { usePersonStore } from "@/stores/person";
import type { AbsenceStatsResponse, UpdatePersonRequest } from "@/types/person";

const personStore = usePersonStore();
const { person } = storeToRefs(personStore);

// Skip the loading skeleton if a previous session already cached a person. checkOnce()
// below trusts that cache outright, so there's nothing to wait for in that case.
const initialLoading = ref(!personStore.hydrated);
const saved = ref(false);
let savedTimeout: ReturnType<typeof setTimeout> | undefined;
const deleting = ref(false);
const deleteError = ref<string | null>(null);
const stats = ref<AbsenceStatsResponse | null>(null);
const statsLoading = ref(false);
const statsYear = ref(new Date().getFullYear());

// Fires alongside checkOnce() below rather than waiting on it: the backend resolves
// "me" from the auth token independently, so there's no need for the frontend to
// already know who the person is before asking for their stats.
const isLatestStatsLoad = useStaleGuard();

watch(
  statsYear,
  async (year) => {
    const isCurrent = isLatestStatsLoad();
    statsLoading.value = true;
    try {
      const result = await getMyStats(year);
      if (!isCurrent()) return;
      stats.value = result;
    } catch (error: unknown) {
      console.error("ERROR => Failed to load absence stats:", error);
    } finally {
      if (isCurrent()) statsLoading.value = false;
    }
  },
  { immediate: true },
);

onMounted(async () => {
  try {
    await personStore.checkOnce();
  } catch (error: unknown) {
    console.error("ERROR => Failed to check for an existing profile:", error);
  } finally {
    initialLoading.value = false;
  }
});

async function handleSave(payload: UpdatePersonRequest, done: (error?: string | null) => void) {
  try {
    const updated = await updateMe(payload);
    personStore.setPerson(updated);

    clearTimeout(savedTimeout);
    saved.value = true;
    savedTimeout = setTimeout(() => {
      saved.value = false;
    }, 2500);
    done();
  } catch (error: unknown) {
    console.error("ERROR => Failed to save profile changes:", error);
    done(
      error instanceof Error ? error.message : "Failed to save profile changes. Please try again.",
    );
  }
}

async function handleDelete() {
  if (!confirm("Delete your profile? This cannot be undone.")) return;

  deleteError.value = null;
  deleting.value = true;
  try {
    await deleteMe();
    personStore.clearPerson();
  } catch (error: unknown) {
    console.error("ERROR => Failed to delete profile:", error);
    deleteError.value = "Failed to delete profile. Please try again.";
  } finally {
    deleting.value = false;
  }
}
</script>
