<template>
  <div ref="rootRef" class="relative">
    <button
      type="button"
      class="relative w-9 h-9 flex items-center justify-center rounded-pill text-secondary hover:text-ink hover:bg-surface/60 transition-colors"
      :data-tooltip="badgeCount > 0 ? `Notifications (${badgeCount} unread)` : 'Notifications'"
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
        v-if="badgeCount > 0"
        class="absolute top-1 right-1 min-w-[16px] h-4 px-1 rounded-pill bg-error-solid text-on-error text-xs font-bold flex items-center justify-center"
      >
        {{ badgeCount > 9 ? "9+" : badgeCount }}
      </span>
    </button>

    <div
      v-if="open"
      class="absolute right-0 mt-2 w-80 max-h-96 overflow-y-auto bg-background border border-border rounded-8 shadow-overlay z-40"
    >
      <div class="px-4 py-3 border-b border-border">
        <h5>Notifications</h5>
      </div>

      <p v-if="loading" class="text-sm text-secondary px-4 py-3">Loading…</p>
      <template v-else>
        <p
          v-if="notifications.length === 0 && pendingApprovals.length === 0"
          class="text-sm text-secondary px-4 py-3"
        >
          Nothing yet.
        </p>

        <!-- Live state, not a one-off event like the notifications below: it isn't marked
             read on open, and an item leaves this list the moment its request is resolved,
             from wherever that happens - here or the approvals page itself. -->
        <div v-if="pendingApprovals.length > 0" class="border-b border-border">
          <p class="px-4 pt-3 pb-1 text-xs font-semibold text-secondary uppercase tracking-wide">
            Waiting on you
          </p>
          <ul class="divide-y divide-border">
            <li v-for="item in pendingApprovals" :key="item.absenceId">
              <button
                type="button"
                class="w-full text-left px-4 py-3 hover:bg-surface transition-colors"
                @click="goToApprovals"
              >
                <p class="text-sm">
                  <span class="font-semibold">{{ item.personName }}</span>
                  — {{ item.type }}, {{ item.startDate }} → {{ item.endDate }}
                </p>
              </button>
            </li>
          </ul>
        </div>

        <ul v-if="notifications.length > 0" class="divide-y divide-border">
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
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { storeToRefs } from "pinia";
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { useRouter } from "vue-router";

import { useNotificationsStore } from "@/stores/notifications";
import type { NotificationType } from "@/types/notification";

// The feed lives in the store (loaded from AppHeader, before this component exists);
// this component is just the panel.
const store = useNotificationsStore();
const { notifications, unreadCount, pendingApprovals, loading } = storeToRefs(store);
const router = useRouter();

// Pending approvals count toward the badge alongside unread reviews: both are things
// this person hasn't acted on yet. They don't count toward markAllRead below though -
// they're not "read", they're resolved, and only the approvals flow does that.
const badgeCount = computed(() => unreadCount.value + pendingApprovals.value.length);

const rootRef = ref<HTMLDivElement | null>(null);
const open = ref(false);

async function toggle() {
  open.value = !open.value;
  if (!open.value) return;

  await store.load();
  if (unreadCount.value === 0) return;

  // Read on open, not per-item: the feed is short enough that "seen the panel" is a fine
  // stand-in for "seen each one". Pending approvals are untouched by this - see badgeCount.
  await store.markAllRead();
}

function goToApprovals() {
  open.value = false;
  router.push("/admin/approvals");
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
