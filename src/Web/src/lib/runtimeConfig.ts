import { appUrl } from "@/lib/appUrl";
import type { RuntimeConfig } from "@/types/config";

/**
 * Branding and IdP settings, served by the API instead of compiled into this bundle, so
 * one build can be deployed by anyone and configured per environment.
 *
 * Loaded once in main.ts before the app mounts, which is why every read afterwards is a
 * plain synchronous call. It can't go through @/api: that attaches an Authorization
 * header, and this is the thing that tells the app how to authenticate in the first place.
 */
let config: RuntimeConfig | null = null;

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const res = await fetch(appUrl("/api/config/bootstrap"), {
    headers: { Accept: "application/json" },
  });
  if (!res.ok) throw new Error(`API error: ${res.status} ${res.statusText}`);

  config = (await res.json()) as RuntimeConfig;
  return config;
}

export function runtimeConfig(): RuntimeConfig {
  if (!config) throw new Error("Runtime config was read before loadRuntimeConfig() finished.");
  return config;
}
