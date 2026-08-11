import { apiFetch, apiJson } from "@/api";

import type { AbsenceDto, AbsencesResponse, CreateAbsenceRequest } from "../types/absence";

export function getAbsences(from: string, to: string): Promise<AbsencesResponse> {
  return apiJson(`/api/absences?from=${from}&to=${to}`);
}

export function createAbsence(request: CreateAbsenceRequest): Promise<AbsenceDto> {
  return apiJson(`/api/absences`, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateAbsence(id: number, request: CreateAbsenceRequest): Promise<AbsenceDto> {
  return apiJson(`/api/absences/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export async function deleteAbsence(id: number): Promise<void> {
  await apiFetch(`/api/absences/${id}`, { method: "DELETE" });
}
