<template>
  <section>
    <h4>Audit log</h4>
    <p class="text-sm text-secondary mt-1">
      Every change to a profile, a manually added bank holiday, or a deleted absence, kept after
      the thing it happened to is gone. Notes are never recorded.
    </p>

    <p v-if="auditLoading && auditEntries.length === 0" class="text-sm text-secondary mt-3">
      Loading…
    </p>

    <table v-else-if="auditEntries.length > 0" class="w-full mt-3 text-sm">
      <thead>
        <tr class="text-left text-xs font-semibold text-secondary border-b border-border">
          <th class="py-2 pr-2 whitespace-nowrap">When</th>
          <th class="py-2 pr-2">Who</th>
          <th class="py-2 pr-2">Action</th>
          <th class="py-2 pr-2">What</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="entry in auditEntries" :key="entry.id" class="border-b border-border">
          <td class="py-2 pr-2 text-secondary whitespace-nowrap">
            {{ formatOccurredAt(entry.occurredAt) }}
          </td>
          <td class="py-2 pr-2">{{ entry.actor }}</td>
          <td class="py-2 pr-2 text-secondary">{{ entry.action }}</td>
          <td class="py-2 pr-2">
            {{ entry.target }}
            <span v-if="entry.details" class="text-secondary">— {{ entry.details }}</span>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-else class="text-sm text-secondary mt-3">Nothing has happened yet.</p>

    <div v-if="auditEntries.length < auditTotal" class="mt-4">
      <button
        type="button"
        class="text-sm text-secondary hover:text-ink transition-colors disabled:opacity-40"
        :disabled="auditLoading"
        @click="loadAudit(true)"
      >
        {{ auditLoading ? "Loading…" : `Show older (${auditTotal - auditEntries.length} more)` }}
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";

import { getAuditLog } from "@/api/admin";
import type { AuditEntryDto } from "@/types/audit";

const AUDIT_PAGE_SIZE = 50;
const auditEntries = ref<AuditEntryDto[]>([]);
const auditTotal = ref(0);
const auditLoading = ref(false);

// Paged by offset from the newest, so "show older" keeps walking back through one log
// rather than re-reading it. Anything written while the page is open shows up on a reload,
// which is soon enough for a log nobody watches live.
async function loadAudit(older = false) {
  auditLoading.value = true;
  try {
    const { entries, total } = await getAuditLog(
      AUDIT_PAGE_SIZE,
      older ? auditEntries.value.length : 0,
    );
    auditEntries.value = older ? [...auditEntries.value, ...entries] : entries;
    auditTotal.value = total;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load the audit log:", error);
  } finally {
    auditLoading.value = false;
  }
}

const occurredAtFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

// The API stores UTC and serializes it without an offset, which the browser would read as
// local time. Marked as UTC before parsing, so the log renders wherever the reader is.
function formatOccurredAt(occurredAt: string): string {
  const utc = /(Z|[+-]\d{2}:\d{2})$/.test(occurredAt) ? occurredAt : `${occurredAt}Z`;
  return occurredAtFormat.format(new Date(utc));
}

onMounted(() => loadAudit());
</script>
