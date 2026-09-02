<template>
  <footer class="sticky bottom-0 border-t border-border px-6 py-4 bg-background">
    <div class="max-w-[1920px] mx-auto flex items-center justify-between h-4">
      <!-- The product's own attribution, not the deployment's. Nothing in configuration
           reaches these values: the name, the mark and the copyright holder are constants
           (lib/productInfo.ts), and a deployment that wants to show its own name does that
           in the header via Team:OrgName. See TRADEMARK.md. -->
      <span class="flex items-center gap-1.5 text-xs text-secondary">
        <BrandMark class="w-3.5 h-3.5" />
        {{ PRODUCT_NAME }} · © {{ year }} {{ PRODUCT_COPYRIGHT_HOLDER }}
      </span>
      <div class="flex items-center gap-4">
        <button class="text-xs text-secondary hover:text-ink transition" @click="helpOpen = true">
          Help
        </button>
        <button class="text-xs text-secondary hover:text-ink transition" @click="openSupport">
          Support
        </button>
        <button
          class="text-xs text-secondary hover:text-ink transition"
          @click="changelogOpen = true"
        >
          v{{ version }}
        </button>
      </div>
    </div>
  </footer>

  <AppModal v-if="helpOpen" title="Help" @close="helpOpen = false">
    <template #tabs>
      <nav class="flex gap-6">
        <button
          v-for="tab in tabs"
          :key="tab"
          class="py-3 text-sm font-semibold border-b-2 transition-colors"
          :class="
            activeTab === tab
              ? 'border-accent text-accent'
              : 'border-transparent text-secondary hover:text-ink hover:border-border'
          "
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </nav>
    </template>
    <MarkdownContent :content="helpContent[activeTab]" />
  </AppModal>

  <AppModal v-if="changelogOpen" title="What's new" @close="changelogOpen = false">
    <MarkdownContent :content="changelogDoc" />
  </AppModal>
</template>

<script setup lang="ts">
import { defineAsyncComponent, ref } from "vue";

import AppModal from "@/components/AppModal.vue";
import BrandMark from "@/components/BrandMark.vue";
import changelogDoc from "@/docs/changelog.md";
import helpDoc from "@/docs/help.md";
import { bootstrapConfig } from "@/lib/bootstrapConfig";
import { PRODUCT_COPYRIGHT_HOLDER, PRODUCT_NAME } from "@/lib/productInfo";

// Async so `marked` (which only this component's modals need) gets its own chunk instead
// of riding in the main bundle; it loads the first time someone opens Help or What's new.
const MarkdownContent = defineAsyncComponent(() => import("@/components/MarkdownContent.vue"));

const { supportUrl, issueUrl } = bootstrapConfig();
const version = __APP_VERSION__;
const year = new Date().getFullYear();

const helpOpen = ref(false);
const changelogOpen = ref(false);

const tabs = ["Documentation", "FAQ"] as const;
const activeTab = ref<(typeof tabs)[number]>("Documentation");

// Split help.md into two sections by h2, filling in the %SUPPORT_URL% placeholder
// so the support link stays configurable per deployment instead of baked into the doc.
function splitHelp(raw: string) {
  const filled = raw.replaceAll("%SUPPORT_URL%", supportUrl);
  const [, docs = "", faq = ""] = filled.split(/^## /m);
  return {
    Documentation: `## ${docs}`.trim(),
    FAQ: `## ${faq}`.trim(),
  };
}

const helpContent = splitHelp(helpDoc);

function openSupport() {
  window.open(issueUrl, "_blank");
}
</script>
