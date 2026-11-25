<template>
  <footer class="sticky bottom-0 border-t border-border px-6 py-4 bg-background">
    <div class="max-w-[1920px] mx-auto flex items-center justify-between flex-wrap gap-y-2">
      <!-- The product's own attribution, not the deployment's. Nothing in configuration
           reaches these values: the name, the mark and the copyright holder are constants
           (lib/productInfo.ts), and a deployment that wants to show its own name does that
           in the header via Team:OrgName. See TRADEMARK.md. -->
      <span class="flex items-center gap-1.5 text-xs text-secondary">
        <BrandMark class="w-3.5 h-3.5" />
        {{ PRODUCT_NAME }} · © {{ year }} {{ PRODUCT_COPYRIGHT_HOLDER }}
      </span>
      <div class="flex items-center gap-4 flex-wrap">
        <a
          v-for="link in repositoryLinks"
          :key="link.label"
          :class="linkClass"
          :href="link.href"
          target="_blank"
          rel="noopener"
        >
          {{ link.label }}
        </a>
        <RouterLink v-for="link in legalLinks" :key="link.label" :class="linkClass" :to="link.to">
          {{ link.label }}
        </RouterLink>
        <a :class="linkClass" :href="licenseUrl" target="_blank" rel="noopener">License</a>
        <a v-if="supportUrl" :class="linkClass" :href="supportUrl" target="_blank" rel="noopener">
          Contact
        </a>
        <a :class="linkClass" :href="changelogUrl" target="_blank" rel="noopener">
          v{{ version }}
        </a>
      </div>
    </div>
  </footer>
</template>

<script setup lang="ts">
import BrandMark from "@/components/BrandMark.vue";
import { appUrl } from "@/lib/appUrl";
import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { PRODUCT_COPYRIGHT_HOLDER, PRODUCT_NAME, REPOSITORY_URL } from "@/lib/productInfo";

// Shared by every footer link, RouterLink or plain <a>, so a link added later can't
// drift from the rest by a missed class.
const linkClass = "text-xs text-secondary hover:underline";

// The documentation lives in the repository (ADR 0019); only the notices this deployment
// fills in with its own operator are served by the app.
const repositoryLinks = [
  { label: "Documentation", href: `${REPOSITORY_URL}/blob/master/docs/user-guide.md` },
  { label: "Install", href: `${REPOSITORY_URL}/blob/master/docs/deployment.md#quickstart` },
  { label: "GitHub", href: REPOSITORY_URL },
];
const changelogUrl = `${REPOSITORY_URL}/blob/master/CHANGELOG.md`;
const legalLinks = [
  { label: "Privacy", to: { path: "/legal", query: { tab: "privacy" } } },
  { label: "Terms", to: { path: "/legal", query: { tab: "terms" } } },
];

const { supportUrl } = bootstrapConfig();
const version = __APP_VERSION__;
const year = new Date().getFullYear();

// appUrl(), not a bare "/LICENSE.txt": a deployment behind ReverseProxy:PathBase serves
// its static files under that sub-path too.
const licenseUrl = appUrl("/LICENSE.txt");
</script>
