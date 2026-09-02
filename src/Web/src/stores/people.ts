import { defineStore, storeToRefs } from "pinia";
import { computed, watch } from "vue";

import { getAllPeople } from "@/api/people";
import { clearCacheGroup } from "@/lib/browserCache";
import { createLoadOnce } from "@/lib/loadOnce";
import { reconcileList, shallowEqual } from "@/lib/reconcile";
import { usePersonStore } from "@/stores/person";
import type { PeopleResponse } from "@/types/person";

// Bump the version suffix whenever PeopleResponse's shape changes: the key carries it,
// so an old browser's copy simply misses and refetches instead of being trusted with
// fields it predates (see stores/config.ts for how that once went wrong).
const PEOPLE_CACHE_KEY = "people.v1";

// Keeps the identity of whatever a refetch didn't change - the calendar grid's row
// v-memo and cell cache compare these identities, so a revalidate that confirms the
// cache must not hand every row a fresh-but-equal object. See lib/reconcile.ts.
function reconcilePeople(previous: PeopleResponse, fresh: PeopleResponse): PeopleResponse {
  const people = reconcileList(previous.people, fresh.people);
  const regionLabels = shallowEqual(previous.regionLabels, fresh.regionLabels)
    ? previous.regionLabels
    : fresh.regionLabels;
  return people === previous.people && regionLabels === previous.regionLabels
    ? previous
    : { people, regionLabels };
}

export const usePeopleStore = defineStore("people", () => {
  // Evicts copies written under older schema versions, which readCache would otherwise
  // never look at again; the current key survives the sweep for the read below.
  clearCacheGroup("people.", PEOPLE_CACHE_KEY);
  const { data, loadOnce, invalidate } = createLoadOnce(
    getAllPeople,
    PEOPLE_CACHE_KEY,
    reconcilePeople,
  );
  const people = computed(() => data.value?.people ?? []);

  // Kept from the same response as the people rather than fetched separately, so the
  // labels can never describe a different set of regions than the one on screen.
  const regionLabels = computed(() => data.value?.regionLabels ?? {});

  // Re-fetch whenever the signed-in user's own profile is created, edited, or deleted,
  // so any already-mounted consumer of `people` reflects it without a manual reload.
  const { version } = storeToRefs(usePersonStore());
  watch(version, () => {
    invalidate();
    loadOnce();
  });

  /** Forces the next read to hit the network. Use this after mutations made outside this store, like admin CRUD. */
  function refresh() {
    invalidate();
    return loadOnce();
  }

  return { people, regionLabels, loadOnce, refresh };
});
