import { defineStore, storeToRefs } from "pinia";
import { computed, watch } from "vue";

import { getAllPeople } from "@/api/people";
import { createLoadOnce } from "@/lib/loadOnce";
import { usePersonStore } from "@/stores/person";

export const usePeopleStore = defineStore("people", () => {
  const { data, loadOnce, invalidate } = createLoadOnce(getAllPeople);
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
