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

/** Mirrors the API's PendingApprovalDto. A live view over what's currently Pending, not a
 * one-time event: it has no id of its own to key off besides the absence it's about, and
 * no read state - it simply stops appearing once the request is resolved. */
export interface PendingApprovalDto {
  absenceId: number;
  personName: string;
  type: AbsenceType;
  startDate: string; // yyyy-MM-dd
  endDate: string;
}

/** Mirrors the API's NotificationsResponse. Newest first. pendingApprovals is empty for
 * anyone without AbsencesApprove, rather than omitted. */
export interface NotificationsResponse {
  notifications: NotificationDto[];
  unreadCount: number;
  pendingApprovals: PendingApprovalDto[];
}
