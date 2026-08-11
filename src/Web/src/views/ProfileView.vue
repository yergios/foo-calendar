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
          :initial-color="person.color"
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
        <p v-if="saved" class="flex items-center gap-1 text-sm text-green mt-2">
          <svg
            xmlns="http://www.w3.org/2000/svg"
            xmlns:xlink="http://www.w3.org/1999/xlink"
            aria-hidden="true"
            role="img"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            style="color: rgb(15, 110, 86); opacity: 1; transform: rotate(0deg)"
          >
            <path
              fill="currentColor"
              d="m10.6 13.8l-2.15-2.15q-.275-.275-.7-.275t-.7.275t-.275.7t.275.7L9.9 15.9q.3.3.7.3t.7-.3l5.65-5.65q.275-.275.275-.7t-.275-.7t-.7-.275t-.7.275zM12 22q-2.075 0-3.9-.788t-3.175-2.137T2.788 15.9T2 12t.788-3.9t2.137-3.175T8.1 2.788T12 2t3.9.788t3.175 2.137T21.213 8.1T22 12t-.788 3.9t-2.137 3.175t-3.175 2.138T12 22"
            ></path></svg
          >Profile saved.
        </p>
        <p v-if="deleteError" class="text-sm text-error mt-2">{{ deleteError }}</p>
      </div>
      <div>
        <YearPicker v-model="statsYear">
          <h5>Absence stats {{ statsYear }}</h5>
        </YearPicker>
        <div v-if="statsLoading" class="grid grid-cols-3 gap-3 mt-3">
          <AppSkeleton class="h-20" />
          <AppSkeleton class="h-20" />
          <AppSkeleton class="h-20" />
        </div>
        <div v-else-if="stats" class="grid grid-cols-3 gap-3 mt-3">
          <div class="bg-surface rounded-12 p-4 text-center">
            <div class="text-2xl font-bold text-primary-dark">{{ stats.vacationDays }}</div>
            <div class="text-xs text-secondary mt-1">Vacation</div>
          </div>
          <div class="bg-surface rounded-12 p-4 text-center">
            <div class="text-2xl font-bold text-primary-dark">{{ stats.sickDays }}</div>
            <div class="text-xs text-secondary mt-1">Sick</div>
          </div>
          <div class="bg-surface rounded-12 p-4 text-center">
            <div class="text-2xl font-bold text-primary-dark">{{ stats.otherDays }}</div>
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
import YearPicker from "@/components/YearPicker.vue";
import { useConfigStore } from "@/stores/config";
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
watch(
  statsYear,
  async (year) => {
    statsLoading.value = true;
    try {
      stats.value = await getMyStats(year);
    } catch (error: unknown) {
      console.error("ERROR => Failed to load absence stats:", error);
    } finally {
      statsLoading.value = false;
    }
  },
  { immediate: true },
);

onMounted(async () => {
  // Pre-warm: ProfileForm needs this too, but can't mount (and so can't fetch it
  // itself) until `person` resolves below. Kicking it off here lets that network
  // round trip overlap with the checkOnce() wait instead of starting after it.
  useConfigStore()
    .loadOnce()
    .catch((error: unknown) => console.error("ERROR => Failed to preload app config:", error));

  try {
    await personStore.checkOnce();
  } catch (error: unknown) {
    console.error("ERROR => Failed to check for an existing profile:", error);
  } finally {
    initialLoading.value = false;
  }
});

async function handleSave(payload: UpdatePersonRequest, done: () => void) {
  try {
    const updated = await updateMe(payload);
    personStore.setPerson(updated);

    clearTimeout(savedTimeout);
    saved.value = true;
    savedTimeout = setTimeout(() => {
      saved.value = false;
    }, 2500);
  } catch (error: unknown) {
    console.error("ERROR => Failed to save profile changes:", error);
  } finally {
    done();
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
