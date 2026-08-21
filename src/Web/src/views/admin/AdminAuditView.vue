<template>
  <section>
    <h4>Audit log</h4>
    <p class="text-sm text-secondary mt-1">
      Every change to a profile, a manually added bank holiday, or a deleted absence, kept after the
      thing it happened to is gone. Notes are never recorded.
    </p>

    <div class="border border-border rounded-12 p-4 mt-4">
      <div class="flex items-center justify-between gap-3 flex-wrap min-h-9">
        <div class="flex items-center gap-3 flex-wrap">
          <input
            v-model="search"
            type="text"
            placeholder="Search by user…"
            class="h-9 w-56 border border-border-strong rounded-8 px-3 text-sm bg-background"
          />
          <select
            v-model="actionFilter"
            class="h-9 border border-border-strong rounded-8 px-2 text-sm bg-background"
          >
            <option value="">All actions</option>
            <option v-for="action in AUDIT_ACTIONS" :key="action" :value="action">
              {{ action }}
            </option>
          </select>
        </div>
        <div class="flex items-center gap-2">
          <input
            v-model="fromDate"
            type="date"
            class="h-9 border border-border-strong rounded-8 px-3 text-sm bg-background"
          />
          <span class="text-secondary text-sm">–</span>
          <input
            v-model="toDate"
            type="date"
            class="h-9 border border-border-strong rounded-8 px-3 text-sm bg-background"
          />
        </div>
      </div>

      <p v-if="auditLoading && auditEntries.length === 0" class="text-sm text-secondary mt-3">
        Loading…
      </p>

      <table v-else-if="auditEntries.length > 0" class="w-full mt-3 text-sm">
        <thead>
          <tr class="text-left text-xs font-semibold text-secondary border-b border-border">
            <th class="py-2 pr-2 whitespace-nowrap">
              <SortButton
                :active="sortBy === 'occurredAt'"
                :descending="descending"
                @toggle="toggleSort('occurredAt')"
                >When ({{ timeZone }})</SortButton
              >
            </th>
            <th class="py-2 pr-2">
              <SortButton
                :active="sortBy === 'actor'"
                :descending="descending"
                @toggle="toggleSort('actor')"
                >Who</SortButton
              >
            </th>
            <th class="py-2 pr-2">
              <SortButton
                :active="sortBy === 'action'"
                :descending="descending"
                @toggle="toggleSort('action')"
                >Action</SortButton
              >
            </th>
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

      <div v-if="total > AUDIT_PAGE_SIZE" class="flex items-center justify-end gap-3 mt-3">
        <button
          type="button"
          class="text-sm text-secondary hover:text-ink transition-colors disabled:opacity-40 disabled:hover:text-secondary"
          :disabled="page === 1 || auditLoading"
          @click="page--"
        >
          ‹ Prev
        </button>
        <span class="text-sm text-secondary">Page {{ page }} of {{ totalPages }}</span>
        <button
          type="button"
          class="text-sm text-secondary hover:text-ink transition-colors disabled:opacity-40 disabled:hover:text-secondary"
          :disabled="page === totalPages || auditLoading"
          @click="page++"
        >
          Next ›
        </button>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";

import { getAuditLog } from "@/api/admin";
import SortButton from "@/components/SortButton.vue";
import { useStaleGuard } from "@/composables/useStaleGuard";
import { parse } from "@/lib/dates";
import { AuditAction } from "@/types/audit";
import type { AuditEntryDto, AuditSortColumn } from "@/types/audit";

const AUDIT_ACTIONS = Object.values(AuditAction);

// Real pagination, same reasoning as the people table: `auditEntries` here is only ever
// one page's worth, fetched fresh from /api/admin/audit on every page/filter/sort change,
// rather than an ever-growing list held in memory.
const AUDIT_PAGE_SIZE = 50;
const page = ref(1);
const total = ref(0);
const auditEntries = ref<AuditEntryDto[]>([]);
const auditLoading = ref(true);
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / AUDIT_PAGE_SIZE)));

// Search, action filter, time range, and sort all narrow or reorder the same underlying
// set, so they all have to be applied server-side, before paging - see AuditLog.ReadAsync.
const search = ref("");
const actionFilter = ref("");
const fromDate = ref("");
const toDate = ref("");
const sortBy = ref<AuditSortColumn>("occurredAt");
const descending = ref(true);

function toggleSort(column: AuditSortColumn) {
  if (sortBy.value === column) {
    descending.value = !descending.value;
    return;
  }
  sortBy.value = column;
  // Time defaults to newest-first, matching today's behavior; Who/Action default
  // ascending, same convention the people table's columns use.
  descending.value = column === "occurredAt";
}

// The day after `date`'s local midnight (`parse`, from lib/dates, already resolves a bare
// "YYYY-MM-DD" to local rather than UTC midnight) - `to` needs this as its exclusive upper
// bound so the whole of the picked end date is included. `setDate`, not a flat 24h of
// milliseconds: it operates on the calendar day field directly, so it stays correct across
// a DST transition, where a day isn't exactly 24 real hours.
function nextLocalMidnight(date: string): Date {
  const next = parse(date);
  next.setDate(next.getDate() + 1);
  return next;
}

const isLatestPageLoad = useStaleGuard();

async function loadPage() {
  const isCurrent = isLatestPageLoad();
  auditLoading.value = true;
  try {
    const result = await getAuditLog(page.value, AUDIT_PAGE_SIZE, {
      search: search.value.trim() || undefined,
      action: actionFilter.value || undefined,
      from: fromDate.value ? parse(fromDate.value).toISOString() : undefined,
      to: toDate.value ? nextLocalMidnight(toDate.value).toISOString() : undefined,
      sortBy: sortBy.value,
      descending: descending.value,
    });
    if (!isCurrent()) return;
    auditEntries.value = result.entries;
    total.value = result.total;
  } catch (error: unknown) {
    console.error("ERROR => Failed to load the audit log:", error);
  } finally {
    if (isCurrent()) auditLoading.value = false;
  }
}

watch(page, loadPage);

// Any of these changing means page 1 might not even be the same 50 entries any more, so
// they all jump back to it rather than staying wherever the user happened to be.
function reloadFromStart() {
  if (page.value === 1) loadPage();
  else page.value = 1;
}

watch([actionFilter, fromDate, toDate, sortBy, descending], reloadFromStart);

// Debounced, unlike the rest: search fires on every keystroke, and a request per keystroke
// would just make the later ones win a race against each other for no benefit.
let searchDebounce: ReturnType<typeof setTimeout> | undefined;
watch(search, () => {
  clearTimeout(searchDebounce);
  searchDebounce = setTimeout(reloadFromStart, 300);
});

// A filter change can leave the current page past the new end, which would otherwise show
// an empty table with no way back.
watch(totalPages, (max) => {
  if (page.value > max) page.value = max;
});

// The API stores and sends UTC with no offset; everything here is rendered in whichever
// zone the browser is already in, so the "When" column says which one that is rather than
// leaving it to be assumed.
const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

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

onMounted(() => loadPage());
</script>
