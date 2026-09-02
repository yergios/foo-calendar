import { defineStore } from "pinia";
import { ref } from "vue";

import { getNotifications, markNotificationsRead } from "@/api/notifications";
import type { NotificationDto } from "@/types/notification";

// A store rather than state local to NotificationBell so the fetch can start from
// AppHeader's mount, in parallel with the person check. The bell itself only renders once
// the person resolves, and on a cold browser that used to chain this request behind
// /api/people/me for no reason: the API resolves the caller from the token and answers an
// empty feed for someone with no profile yet.
export const useNotificationsStore = defineStore("notifications", () => {
  const notifications = ref<NotificationDto[]>([]);
  const unreadCount = ref(0);
  const loading = ref(false);

  async function load() {
    loading.value = true;
    try {
      const response = await getNotifications();
      notifications.value = response.notifications;
      unreadCount.value = response.unreadCount;
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

  return { notifications, unreadCount, loading, load, markAllRead };
});
