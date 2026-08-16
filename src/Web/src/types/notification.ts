import type { AbsenceType } from "./absence";

export type NotificationType = "AbsenceApproved" | "AbsenceRejected";

/** Mirrors the API's NotificationDto. Denormalized from the absence it describes, so it
 * still reads correctly however that absence was later edited or deleted. */
export interface NotificationDto {
  id: number;
  type: NotificationType;
  absenceType: AbsenceType;
  startDate: string; // yyyy-MM-dd
  endDate: string;
  /** The reviewer's reason. Only ever set for a rejection. */
  note: string | null;
  createdAt: string;
  readAt: string | null;
}

/** Mirrors the API's NotificationsResponse. Newest first. */
export interface NotificationsResponse {
  notifications: NotificationDto[];
  unreadCount: number;
}
