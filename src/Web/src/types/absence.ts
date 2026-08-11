export type AbsenceType = "Vacation" | "Sick" | "Other";

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
