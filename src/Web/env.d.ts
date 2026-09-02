/// <reference types="vite/client" />
declare const __APP_VERSION__: string;

interface Window {
  /** Set by the inline script in index.html, which starts the bootstrap config fetch
   * during HTML parse so it overlaps the bundle download. Consumed (and cleared) by
   * loadRuntimeConfig. */
  __earlyBootstrapConfig?: Promise<Response>;
}
