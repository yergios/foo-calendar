import { apiFetch, apiJson } from "@/api";

import type { SyncBankHolidaysResponse } from "../types/bankHoliday";
import type {
  AllAbsenceStatsResponse,
  CreatePersonRequest,
  PersonDto,
  UpdatePersonRequest,
} from "../types/person";

export function createPerson(request: CreatePersonRequest): Promise<PersonDto> {
  return apiJson(`/api/admin/people`, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updatePerson(id: number, request: UpdatePersonRequest): Promise<PersonDto> {
  return apiJson(`/api/admin/people/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export async function deletePerson(id: number): Promise<void> {
  await apiFetch(`/api/admin/people/${id}`, { method: "DELETE" });
}

export function getAllPeopleStats(year: number): Promise<AllAbsenceStatsResponse> {
  return apiJson(`/api/admin/people/stats?year=${year}`);
}

export function syncBankHolidays(year: number): Promise<SyncBankHolidaysResponse> {
  return apiJson(`/api/admin/bank-holidays/sync?year=${year}`, {
    method: "POST",
  });
}
