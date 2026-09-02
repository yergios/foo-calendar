/// <reference types="vite/client" />
declare const __APP_VERSION__: string;

interface Window {
  /** Inlined into the SPA shell by the API (see SpaShell.cs), so the app knows how to
   * sign in without a round trip gating its own mount. Absent when the Vite dev server
   * serves index.html itself, which is what the fetch fallback in bootstrapConfig.ts
   * covers. */
  __bootstrap?: import("@/types/config").BootstrapConfig;
}
