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
  // index.html starts this fetch during HTML parse so it overlaps the bundle download.
  // A failed early fetch falls back to a fresh request rather than surfacing an error the
  // retry would have absorbed; a non-OK response still throws below, unchanged.
  const early = window.__earlyBootstrapConfig;
  window.__earlyBootstrapConfig = undefined;
  let res = early ? await early.catch(() => null) : null;
  res ??= await fetch(appUrl("/api/config/bootstrap"), {
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
