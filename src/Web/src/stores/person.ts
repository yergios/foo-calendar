import { defineStore } from "pinia";
import { ref } from "vue";

import { createMe, tryGetMe } from "@/api/people";
import { chooseIdentity } from "@/auth";
import { clearCache, clearCacheGroup, readCache, writeCache } from "@/lib/browserCache";
import { PERSON_CACHE_KEY } from "@/stores/personCacheKey";
import type { OnboardingRequest, PersonDto } from "@/types/person";

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

  /**
   * Finishes onboarding, which on a deployment with no identity provider is also where
   * identity is decided: the username the form collected is claimed first, because every
   * request after it - including the two below - is resolved as whoever that cookie names.
   *
   * Claiming a name that already has a profile is how someone who cleared their cookie
   * gets back to their own, so an existing profile is adopted rather than treated as a
   * collision. That is the whole of "signing in" here.
   */
  async function completeOnboarding({ username, ...profile }: OnboardingRequest) {
    if (username) {
      await chooseIdentity(username);
      hydrated.value = false;
      person.value = null;

      const existing = await tryGetMe();
      if (existing) {
        setPerson(existing);
        showOnboarding.value = false;
        resolveOnboarding?.(existing);
        resolveOnboarding = null;
        return;
      }
    }

    const created = await createMe(profile);
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
