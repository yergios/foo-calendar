import { apiFetch, apiFetchRaw, apiJson, errorForResponse } from "@/api";

import type {
  AbsenceStatsResponse,
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
