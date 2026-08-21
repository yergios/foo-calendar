import { apiFetch, apiFetchRaw, apiJson, errorForResponse } from "@/api";

import type {
  AbsenceStatsResponse,
  PeoplePageResponse,
  PeopleResponse,
  PersonDto,
  UpdatePersonRequest,
} from "../types/person";

export function createMe(request: UpdatePersonRequest): Promise<PersonDto> {
  return apiJson(`/api/people`, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function getAllPeople(): Promise<PeopleResponse> {
  return apiJson(`/api/people`);
}

/** For the admin people list, which pages instead of holding the whole roster in memory
 * the way the calendar's shared people store needs to. */
export function getPeoplePage(page: number, pageSize: number): Promise<PeoplePageResponse> {
  return apiJson(`/api/people/paged?page=${page}&pageSize=${pageSize}`);
}

export function getMyStats(year?: number): Promise<AbsenceStatsResponse> {
  const query = year ? `?year=${year}` : "";
  return apiJson(`/api/people/me/stats${query}`);
}

export function updateMe(request: UpdatePersonRequest): Promise<PersonDto> {
  return apiJson(`/api/people/me`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export async function deleteMe(): Promise<void> {
  await apiFetch(`/api/people/me`, { method: "DELETE" });
}

export async function tryGetMe(): Promise<PersonDto | null> {
  const res = await apiFetchRaw(`/api/people/me`);
  if (res.status === 404) return null;
  if (!res.ok) throw await errorForResponse(res);
  return res.json();
}
