import { apiFetch, apiJson } from "@/api";

import type { NotificationsResponse } from "../types/notification";

export function getNotifications(): Promise<NotificationsResponse> {
  return apiJson(`/api/notifications`);
}

export async function markNotificationsRead(): Promise<void> {
  await apiFetch(`/api/notifications/read`, { method: "POST" });
}
