<template>
  <footer class="sticky bottom-0 border-t border-border px-6 py-4 bg-background">
    <div class="max-w-[1920px] mx-auto flex items-center justify-between h-4">
      <span class="text-xs text-secondary">© {{ year }} {{ appName }}. All rights reserved.</span>
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
    <MarkdownContent :content="changelogContent" />
  </AppModal>
</template>

<script setup lang="ts">
import { ref } from "vue";

import AppModal from "@/components/AppModal.vue";
import MarkdownContent from "@/components/MarkdownContent.vue";
import changelogDoc from "@/docs/changelog.md";
import helpDoc from "@/docs/help.md";

const issueUrl = import.meta.env.VITE_ISSUE_URL;
const supportUrl = import.meta.env.VITE_SUPPORT_URL;
const appName = import.meta.env.VITE_APP_NAME ?? "Foo Calendar";
const version = __APP_VERSION__;
const year = new Date().getFullYear();

const helpOpen = ref(false);
const changelogOpen = ref(false);

const tabs = ["Documentation", "FAQ"] as const;
const activeTab = ref<(typeof tabs)[number]>("Documentation");

// Split help.md into two sections by h2, filling in the %VITE_SUPPORT_URL% placeholder
// so the support link stays configurable per deployment instead of baked into the doc.
function splitHelp(raw: string) {
  const filled = raw.replaceAll("%VITE_SUPPORT_URL%", supportUrl ?? "");
  const [, docs = "", faq = ""] = filled.split(/^## /m);
  return {
    Documentation: `## ${docs}`.trim(),
    FAQ: `## ${faq}`.trim(),
  };
}

const helpContent = splitHelp(helpDoc);
const changelogContent = changelogDoc;

const openSupport = () => {
  window.open(issueUrl, "_blank");
};
</script>
