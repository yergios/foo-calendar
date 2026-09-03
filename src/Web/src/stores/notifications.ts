import { defineStore } from "pinia";
import { ref } from "vue";

import { getNotifications, markNotificationsRead } from "@/api/notifications";
import type { NotificationDto, PendingApprovalDto } from "@/types/notification";

// A store rather than state local to NotificationBell so the fetch can start from
// AppHeader's mount, in parallel with the person check. The bell itself only renders once
// the person resolves, and on a cold browser that used to chain this request behind
// /api/people/me for no reason: the API resolves the caller from the token and answers an
// empty feed for someone with no profile yet.
export const useNotificationsStore = defineStore("notifications", () => {
  const notifications = ref<NotificationDto[]>([]);
  const unreadCount = ref(0);
  // Empty for anyone without AbsencesApprove - the API decides that, not this store. Also
  // read by AdminLayout for the approvals nav badge, so this one store is the single
  // source both places agree on.
  const pendingApprovals = ref<PendingApprovalDto[]>([]);
  const loading = ref(false);

  async function load() {
    loading.value = true;
    try {
      const response = await getNotifications();
      notifications.value = response.notifications;
      unreadCount.value = response.unreadCount;
      pendingApprovals.value = response.pendingApprovals;
    } catch (error: unknown) {
      console.error("ERROR => Failed to load notifications:", error);
    } finally {
      loading.value = false;
    }
  }

  async function markAllRead() {
    try {
      await markNotificationsRead();
      unreadCount.value = 0;
    } catch (error: unknown) {
      console.error("ERROR => Failed to mark notifications read:", error);
    }
  }

  // Called by the approvals page right after it resolves one itself, so the badge and the
  // bell's list update in the same frame as the action that caused it rather than waiting
  // for this store's next load().
  function removePendingApproval(absenceId: number) {
    pendingApprovals.value = pendingApprovals.value.filter((p) => p.absenceId !== absenceId);
  }

  return {
    notifications,
    unreadCount,
    pendingApprovals,
    loading,
    load,
    markAllRead,
    removePendingApproval,
  };
});
