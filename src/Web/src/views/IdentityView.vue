<template>
  <div class="min-h-screen flex flex-col items-center justify-center gap-6 px-6">
    <div class="flex flex-col items-center gap-2 text-center">
      <BrandMark class="w-10 h-10 text-primary-text" />
      <h1 class="font-display font-bold text-2xl text-ink">Who are you?</h1>
      <p class="text-sm text-secondary max-w-md">
        This calendar has no sign-in. Pick your name so your absences land on your own row. Anyone
        on this network can pick any name here.
      </p>
    </div>

    <div class="w-full max-w-sm flex flex-col gap-3">
      <div v-if="people.length > 0" class="flex flex-col gap-1.5 max-h-72 overflow-y-auto">
        <button
          v-for="person in people"
          :key="person.username"
          type="button"
          class="text-left border border-border rounded-8 px-3 py-2 hover:bg-surface transition-colors disabled:opacity-50"
          :disabled="busy"
          @click="choose(person.username)"
        >
          <span class="text-sm text-ink">{{ person.displayName || person.username }}</span>
          <span class="text-xs text-secondary ml-2">{{ person.username }}</span>
        </button>
      </div>

      <form
        class="flex flex-col gap-2 border-t border-border pt-3"
        @submit.prevent="choose(newUsername)"
      >
        <label for="newUsername" class="text-xs font-semibold text-secondary">
          {{ people.length > 0 ? "Not listed? Enter a username" : "Enter a username to start" }}
        </label>
        <div class="flex gap-2">
          <input
            id="newUsername"
            v-model="newUsername"
            type="text"
            autocomplete="username"
            class="flex-1 min-w-0 border border-border-strong rounded-8 px-3 py-2 bg-background text-sm"
            placeholder="jdoe"
          />
          <button
            type="submit"
            class="bg-primary text-on-primary rounded-8 px-3 py-2 text-sm font-semibold hover:bg-primary-hover transition-colors disabled:opacity-50"
            :disabled="busy || newUsername.trim().length === 0"
          >
            Continue
          </button>
        </div>
      </form>

      <p v-if="error" class="text-xs text-error">{{ error }}</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from "vue";

import { chooseIdentity, identityOptions } from "@/auth";
import BrandMark from "@/components/BrandMark.vue";

// Read once: the list was fetched before the guard sent anyone here, and choosing reloads
// the page anyway, so there is nothing for it to react to.
const people = identityOptions();

const newUsername = ref("");
const busy = ref(false);
const error = ref("");

async function choose(username: string) {
  const trimmed = username.trim();
  if (trimmed.length === 0 || busy.value) return;

  busy.value = true;
  error.value = "";
  try {
    await chooseIdentity(trimmed);
  } catch (err: unknown) {
    console.error("ERROR => Failed to choose an identity:", err);
    error.value = "That didn't work. Try again.";
    busy.value = false;
  }
}
</script>
