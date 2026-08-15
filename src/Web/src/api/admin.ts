import { apiFetch, apiJson } from "@/api";

import type { AuditEntriesResponse } from "../types/audit";
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

/** Newest first. `offset` pages further back through the same log. */
export function getAuditLog(limit: number, offset = 0): Promise<AuditEntriesResponse> {
  return apiJson(`/api/admin/audit?limit=${limit}&offset=${offset}`);
}

/** Omitting the region syncs every region the team occupies; naming one re-runs just that
 * region's country, which is what retrying a single failure needs. */
export function syncBankHolidays(
  year: number,
  region?: string | null,
): Promise<SyncBankHolidaysResponse> {
  const regionQuery = region ? `&region=${encodeURIComponent(region)}` : "";
  return apiJson(`/api/admin/bank-holidays/sync?year=${year}${regionQuery}`, {
    method: "POST",
  });
}
