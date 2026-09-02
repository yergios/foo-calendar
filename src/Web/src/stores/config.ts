import { defineStore } from "pinia";

import { getAppConfig } from "@/api/config";
import { createLoadOnce } from "@/lib/loadOnce";
import type { AppConfig } from "@/types/config";

// Deliberately no localStorage cacheKey: AppConfig's shape once changed across a
// redeploy and silently stranded a browser's stale cached copy with a missing field
// indefinitely. Version-suffixed keys plus stale-while-revalidate (what usePeopleStore
// does now, see PEOPLE_CACHE_KEY) are the fix for exactly that failure mode, but this
// request is small and cheap enough that the cross-reload instant-paint isn't worth even
// the key-bumping discipline. In-memory dedup for the session is enough.
export const useConfigStore = defineStore("config", () => {
  const { data: config, loadOnce } = createLoadOnce<AppConfig>(getAppConfig);
  return { config, loadOnce };
});
