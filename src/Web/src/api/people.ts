import { apiFetch, apiFetchRaw, apiJson, errorForResponse } from "@/api";

import type {
  AbsenceStatsResponse,
  PeoplePageQuery,
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
 * the way the calendar's shared people store needs to. Search, region filter, and sort all
 * happen server-side, since they have to be applied before paging, not after. */
export function getPeoplePage(
  page: number,
  pageSize: number,
  query: PeoplePageQuery = {},
): Promise<PeoplePageResponse> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (query.search) params.set("search", query.search);
  for (const region of query.regions ?? []) params.append("regions", region);
  if (query.sortBy) params.set("sortBy", query.sortBy);
  // Sent explicitly either way, not just when true: the API's own default only happens to
  // match "omitted" for this endpoint, and leaving that unstated is exactly what made the
  // audit log's equivalent silently sort descending no matter what this sent (that
  // endpoint's default direction is the opposite one).
  if (query.descending !== undefined) params.set("sortDir", query.descending ? "desc" : "asc");
  if (query.statsYear !== undefined) params.set("year", String(query.statsYear));

  return apiJson(`/api/people/paged?${params.toString()}`);
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
