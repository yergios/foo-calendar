import "@fontsource/inter/400.css";
import "@fontsource/inter/600.css";
import "@fontsource/nunito/700.css";
import { createPinia } from "pinia";
import { createApp } from "vue";

import { initAuth } from "@/auth";
import { loadBootstrapConfig } from "@/lib/bootstrapConfig";
import router from "@/router";

import App from "./App.vue";
import "./styles/index.css";

// Nothing can render before the app knows how to sign in. That normally costs nothing:
// the API inlines the answer into the shell, so this resolves without touching the
// network (see lib/bootstrapConfig.ts). The title is already correct in the HTML, since
// the product's name is a constant rather than something a deployment configures.
async function bootstrap() {
  const config = await loadBootstrapConfig();

  initAuth(config.auth);

  const app = createApp(App);
  const pinia = createPinia();
  app.use(router).use(pinia);
  app.mount("#app");
}

bootstrap().catch((error: unknown) => {
  console.error("ERROR => Failed to load app configuration:", error);

  // Vue never mounted, so this can't be a component. The stylesheet is already loaded,
  // which is enough to render the same shape as ErrorLayout.vue by hand.
  const root = document.getElementById("app");
  if (root) {
    root.innerHTML = `
      <div class="min-h-screen flex flex-col items-center justify-center gap-4">
        <span class="font-display font-bold text-6xl text-primary-text opacity-20">500</span>
        <p class="text-secondary text-sm">The application could not load its configuration.</p>
      </div>`;
  }
});
