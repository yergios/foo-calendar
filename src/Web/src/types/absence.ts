export type AbsenceType = "Vacation" | "Sick" | "Other";

/** Single-letter marker painted into a calendar cell. Keyed by the label the legend shows. */
export const ABSENCE_TYPE_INITIALS: Record<AbsenceType, string> = {
  Vacation: "V",
  Sick: "S",
  Other: "O",
};

export interface AbsenceDto {
  id: number;
  personId: number;
  startDate: string; // yyyy-MM-dd
  endDate: string;
  type: AbsenceType;
  note: string | null;
  createdAt: string;
}

export interface AbsencesResponse {
  absences: AbsenceDto[];
}

export interface CreateAbsenceRequest {
  startDate: string;
  endDate: string;
  type: AbsenceType;
  note?: string | null;
}
