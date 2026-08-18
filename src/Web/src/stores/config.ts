import { defineStore } from "pinia";

import { getAppConfig } from "@/api/config";
import { createLoadOnce } from "@/lib/loadOnce";
import type { AppConfig } from "@/types/config";

// Deliberately no localStorage cacheKey (contrast usePersonStore/usePeopleStore, which
// have one): AppConfig is entirely server-authoritative and its shape has already changed
// across a redeploy once, silently stranding a browser's stale cached copy with a missing
// field indefinitely (createLoadOnce trusts a cached value outright, with no TTL or
// version to invalidate it against). In-memory dedup for the session is enough; the
// request itself is small and cheap enough that losing the cross-reload instant-paint
// isn't worth reintroducing that failure mode.
export const useConfigStore = defineStore("config", () => {
  const { data: config, loadOnce } = createLoadOnce<AppConfig>(getAppConfig);
  return { config, loadOnce };
});
