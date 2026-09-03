import { defineStore } from "pinia";
import { ref } from "vue";

import { createMe, tryGetMe } from "@/api/people";
import { clearCache, clearCacheGroup, readCache, writeCache } from "@/lib/browserCache";
import type { PersonDto, UpdatePersonRequest } from "@/types/person";

// Bump the version suffix whenever PersonDto's shape changes: the key carries it, so an old
// browser's copy simply misses and refetches instead of being trusted with fields it
// predates. v3 dropped `email` and the EmailOn* preferences along with email notifications
// entirely; a v2 copy still carrying them is harmless since nothing reads those fields
// anymore, but there's no reason to keep serving it either.
const PERSON_CACHE_KEY = "person.v3";

export const usePersonStore = defineStore("person", () => {
  // Evicts copies written under older schema versions, including the unversioned "person"
  // this key replaced; the current one survives the sweep for the read below. Same shape as
  // usePeopleStore's, and the reason both exist is in stores/config.ts.
  clearCacheGroup("person", PERSON_CACHE_KEY);
  const person = ref<PersonDto | null>(readCache<PersonDto>(PERSON_CACHE_KEY));
  // True once we have a cached profile carried over from a previous session. We trust a
  // cached profile outright, with no network call, because the only things that ever
  // change it are mutations made in this same browser (save, delete, onboarding), and
  // those already update the cache directly through setPerson/clearPerson.
  const hydrated = ref(person.value !== null);
  const showOnboarding = ref(false);

  // Bumped only on genuine mutations (create, update, delete), never by checkOnce()'s
  // first-time lookup. That lets other components watch it to know when to refresh
  // dependent data, without racing the initial population of `person`.
  const version = ref(0);

  let resolveOnboarding: ((p: PersonDto | null) => void) | null = null;
  let pendingCheck: Promise<PersonDto | null> | null = null;

  function hydrate(p: PersonDto | null) {
    person.value = p;
    hydrated.value = true;
    if (p) writeCache(PERSON_CACHE_KEY, p);
    else clearCache(PERSON_CACHE_KEY);
  }

  function setPerson(p: PersonDto | null) {
    hydrate(p);
    version.value++;
  }

  /**
   * Resolves the current person without ever hitting the network if a cached profile
   * already exists (see `hydrated` above). Otherwise it asks the backend at most once
   * per session, and caches the result both in memory and in localStorage. Concurrent
   * callers, like AppHeader and a routed view mounting at the same time, share the same
   * in-flight request instead of each firing their own.
   */
  function checkOnce(): Promise<PersonDto | null> {
    if (hydrated.value) return Promise.resolve(person.value);

    pendingCheck ??= tryGetMe()
      .then((p) => {
        hydrate(p);
        return p;
      })
      .finally(() => {
        pendingCheck = null;
      });

    return pendingCheck;
  }

  /**
   * Resolves with the current person, prompting onboarding first if none exists yet.
   * Resolves with null if the user closes the onboarding modal without submitting.
   */
  async function ensurePerson(): Promise<PersonDto | null> {
    let existing: PersonDto | null = null;
    try {
      existing = await checkOnce();
    } catch (error: unknown) {
      // The API is unreachable, so we can't tell if a profile already exists. Fall
      // through to onboarding instead of leaving the "Set up profile" click dead.
      console.error("ERROR => Failed to check for an existing profile:", error);
    }
    if (existing) return existing;

    showOnboarding.value = true;
    return new Promise<PersonDto | null>((resolve) => {
      resolveOnboarding = resolve;
    });
  }

  async function completeOnboarding(payload: UpdatePersonRequest) {
    const created = await createMe(payload);
    setPerson(created);
    showOnboarding.value = false;
    resolveOnboarding?.(created);
    resolveOnboarding = null;
  }

  function cancelOnboarding() {
    showOnboarding.value = false;
    resolveOnboarding?.(null);
    resolveOnboarding = null;
  }

  /** Forgets the current profile after it's been deleted. */
  function clearPerson() {
    setPerson(null);
  }

  return {
    person,
    hydrated,
    showOnboarding,
    version,
    checkOnce,
    ensurePerson,
    completeOnboarding,
    cancelOnboarding,
    setPerson,
    clearPerson,
  };
});
