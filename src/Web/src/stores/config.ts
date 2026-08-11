import { defineStore } from "pinia";

import { getAppConfig } from "@/api/config";
import { createLoadOnce } from "@/lib/loadOnce";
import type { AppConfig } from "@/types/config";

// Config only ever changes on a backend redeploy, so we trust a cached value outright.
// No extra revalidation on top of what createLoadOnce already dedupes and caches.
export const useConfigStore = defineStore("config", () => {
  const { data: config, loadOnce } = createLoadOnce<AppConfig>(getAppConfig, "config");
  return { config, loadOnce };
});
