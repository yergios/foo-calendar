// No "Sick": an absence's type is visible to the whole team, and "this person is ill"
// is health data under GDPR Article 9. Illness is recorded as Other, which says someone
// is away without saying why. Mirrors the API's AbsenceType.
export type AbsenceType = "Vacation" | "Other" | "Appointment";

/** Single-letter marker painted into a calendar cell. Keyed by the label the legend shows;
 * HomeView.vue's legend iterates this object directly, so key order here is legend order. */
export const ABSENCE_TYPE_INITIALS: Record<AbsenceType, string> = {
  Vacation: "V",
  Appointment: "A",
  Other: "O",
};

export interface AbsenceDto {
  id: number;
  personId: number;
  startDate: string; // yyyy-MM-dd
  endDate: string;
  type: AbsenceType;
  /** Only ever the viewer's own: the API leaves it null on everyone else's absences. */
  note: string | null;
  createdAt: string;
  startTime: string | null; // "HH:mm:ss", set only when type === "Appointment"
  endTime: string | null;
}

export interface AbsencesResponse {
  absences: AbsenceDto[];
}

export interface CreateAbsenceRequest {
  startDate: string;
  endDate: string;
  type: AbsenceType;
  note?: string | null;
  startTime?: string | null;
  endTime?: string | null;
}
