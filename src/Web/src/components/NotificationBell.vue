<template>
  <div ref="rootRef" class="relative">
    <button
      type="button"
      class="relative w-9 h-9 flex items-center justify-center rounded-pill text-secondary hover:text-ink hover:bg-surface/60 transition-colors"
      :title="unreadCount > 0 ? `Notifications (${unreadCount} unread)` : 'Notifications'"
      @click="toggle"
    >
      <svg
        xmlns="http://www.w3.org/2000/svg"
        class="w-5 h-5"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
      >
        <path
          d="M18 8a6 6 0 10-12 0c0 7-3 9-3 9h18s-3-2-3-9"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path d="M13.73 21a2 2 0 01-3.46 0" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
      <span
        v-if="unreadCount > 0"
        class="absolute top-1 right-1 min-w-[16px] h-4 px-1 rounded-pill bg-error text-white text-[10px] font-bold flex items-center justify-center"
      >
        {{ unreadCount > 9 ? "9+" : unreadCount }}
      </span>
    </button>

    <div
      v-if="open"
      class="absolute right-0 mt-2 w-80 max-h-96 overflow-y-auto bg-background border border-border rounded-12 shadow-card z-40"
    >
      <div class="px-4 py-3 border-b border-border">
        <h5>Notifications</h5>
      </div>

      <p v-if="loading" class="text-sm text-secondary px-4 py-3">Loading…</p>
      <p v-else-if="notifications.length === 0" class="text-sm text-secondary px-4 py-3">
        Nothing yet.
      </p>

      <ul v-else class="divide-y divide-border">
        <li v-for="notification in notifications" :key="notification.id" class="px-4 py-3">
          <p class="text-sm">
            <span class="font-semibold" :class="statusClass(notification.type)">{{
              statusLabel(notification.type)
            }}</span>
            — {{ notification.absenceType }}, {{ notification.startDate }} →
            {{ notification.endDate }}
          </p>
          <p v-if="notification.note" class="text-xs text-secondary mt-1">
            "{{ notification.note }}"
          </p>
          <p class="text-xs text-secondary mt-1">{{ relativeTime(notification.createdAt) }}</p>
        </li>
      </ul>
    </div>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { onBeforeUnmount, onMounted, ref } from "vue";

import { useNotificationsStore } from "@/stores/notifications";
import type { NotificationType } from "@/types/notification";

// The feed lives in the store (loaded from AppHeader, before this component exists);
// this component is just the panel.
const store = useNotificationsStore();
const { notifications, unreadCount, loading } = storeToRefs(store);

const rootRef = ref<HTMLDivElement | null>(null);
const open = ref(false);

async function toggle() {
  open.value = !open.value;
  if (!open.value) return;

  await store.load();
  if (unreadCount.value === 0) return;

  // Read on open, not per-item: the feed is short enough that "seen the panel" is a fine
  // stand-in for "seen each one".
  await store.markAllRead();
}

function handleOutsideClick(event: MouseEvent) {
  if (open.value && rootRef.value && !rootRef.value.contains(event.target as Node)) {
    open.value = false;
  }
}

onMounted(() => {
  document.addEventListener("click", handleOutsideClick);
});

onBeforeUnmount(() => {
  document.removeEventListener("click", handleOutsideClick);
});

function statusLabel(type: NotificationType): string {
  return type === "AbsenceApproved" ? "Approved" : "Rejected";
}

function statusClass(type: NotificationType): string {
  return type === "AbsenceApproved" ? "text-accent" : "text-error";
}

const relativeFormat = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

// The API stores UTC and serializes it without an offset, which the browser would read as
// local time, same reasoning as AdminAuditView's audit timestamps.
function relativeTime(createdAt: string): string {
  const utc = /(Z|[+-]\d{2}:\d{2})$/.test(createdAt) ? createdAt : `${createdAt}Z`;
  const diffMs = new Date(utc).getTime() - Date.now();
  const diffMinutes = Math.round(diffMs / 60_000);

  if (Math.abs(diffMinutes) < 60) return relativeFormat.format(diffMinutes, "minute");
  const diffHours = Math.round(diffMinutes / 60);
  if (Math.abs(diffHours) < 24) return relativeFormat.format(diffHours, "hour");
  return relativeFormat.format(Math.round(diffHours / 24), "day");
}
</script>
