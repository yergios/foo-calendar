import { apiJson } from "@/api";

import type { AppConfig } from "../types/config";

export function getAppConfig(): Promise<AppConfig> {
  return apiJson(`/api/config`);
}
