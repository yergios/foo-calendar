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
        <RouterLink v-for="link in docLinks" :key="link.label" :class="linkClass" :to="link.to">
          {{ link.label }}
        </RouterLink>
        <a :class="linkClass" :href="licenseUrl" target="_blank">License</a>
        <a
          v-if="REPOSITORY_URL"
          :class="linkClass"
          :href="REPOSITORY_URL"
          target="_blank"
          rel="noopener"
        >
          GitHub
        </a>
        <a v-if="supportUrl" :class="linkClass" :href="supportUrl" target="_blank">Contact</a>
        <RouterLink :class="linkClass" :to="{ path: '/docs', query: { tab: 'changelog' } }">
          v{{ version }}
        </RouterLink>
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

const docLinks = [
  { label: "Documentation", to: "/docs" },
  { label: "Installation", to: { path: "/docs", query: { tab: "installation" } } },
  { label: "Privacy", to: { path: "/docs", query: { tab: "privacy" } } },
  { label: "Terms", to: { path: "/docs", query: { tab: "terms" } } },
];

const { supportUrl } = bootstrapConfig();
const version = __APP_VERSION__;
const year = new Date().getFullYear();

// appUrl(), not a bare "/LICENSE.txt": a deployment behind ReverseProxy:PathBase serves
// its static files under that sub-path too.
const licenseUrl = appUrl("/LICENSE.txt");
</script>
