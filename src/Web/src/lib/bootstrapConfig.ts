import { appUrl } from "@/lib/appUrl";
import type { BootstrapConfig } from "@/types/config";

/**
 * What the app needs before it has authenticated anyone: how to sign in, and the two
 * links the /401 and /500 pages offer. Those routes live outside the authenticated
 * layout and have to render when signing in is exactly what failed, which is why the
 * support links are here and not in the config store.
 *
 * Normally there is nothing to load: the API inlines this into the SPA shell it serves
 * (see SpaShell.cs), so it is already in the document by the time this module runs and
 * the app mounts without waiting on the network at all. The fetch below is the dev path,
 * where the Vite dev server serves index.html itself and no shell of ours exists.
 *
 * It can't go through @/api: that attaches an Authorization header, and this is the thing
 * that tells the app how to authenticate in the first place.
 */
let config: BootstrapConfig | null = null;

export async function loadBootstrapConfig(): Promise<BootstrapConfig> {
  if (window.__bootstrap) {
    config = window.__bootstrap;
    return config;
  }

  const res = await fetch(appUrl("/api/config/bootstrap"), {
    headers: { Accept: "application/json" },
  });
  if (!res.ok) throw new Error(`API error: ${res.status} ${res.statusText}`);

  config = (await res.json()) as BootstrapConfig;
  return config;
}

export function bootstrapConfig(): BootstrapConfig {
  if (!config) throw new Error("Bootstrap config was read before loadBootstrapConfig() finished.");
  return config;
}
