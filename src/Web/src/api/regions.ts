import { apiJson } from "@/api";

import type { AvailableRegionsResponse } from "../types/config";

export function getAvailableCountries(): Promise<AvailableRegionsResponse> {
  return apiJson(`/api/regions/countries`);
}

/** Subdivisions of one country. Fetched only once a country is picked: most of the
 * catalogue is never looked at, and most countries have no subdivisions at all. */
export function getAvailableRegions(countryCode: string): Promise<AvailableRegionsResponse> {
  return apiJson(`/api/regions/${countryCode}`);
}
