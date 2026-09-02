export interface PeopleResponse {
  people: PersonDto[];
  /** Display names for every region the team occupies and the countries they sit in.
   * Sent with the people because it changes with them, unlike anything in AppConfig. */
  regionLabels: Record<string, string>;
}

/** total is the whole team, not this page - same shape as AuditEntriesResponse. */
export interface PeoplePageResponse {
  people: PersonDto[];
  total: number;
}

/** Matches the API's PersonSortColumn constants (Api/Services/PersonSortColumn.cs) -
 * vacation/other are gated server-side behind ReportsRead, since they carry the same
 * per-year numbers the stats endpoint does. */
export type PeopleSortColumn = "name" | "username" | "homeCity" | "vacation" | "other";

export interface PeoplePageQuery {
  search?: string;
  /** A region code matches its own people and anyone in a subdivision of it, same rule as
   * the home page's region filter. */
  regions?: string[];
  sortBy?: PeopleSortColumn;
  descending?: boolean;
  /** Which year vacation/other are computed for. Ignored unless sortBy is one of them. */
  statsYear?: number;
}

export interface PersonDto {
  id: number;
  username: string;
  displayName: string;
  homeCity: string | null;
  // Only present on the /me responses (GetMe/CreateMe/UpdateMe serialize the raw entity);
  // the colleagues list from getAllPeople() never carries these, same as it never carries
  // email itself.
  emailOnAbsenceReviewed?: boolean;
  emailOnNewAbsenceRequest?: boolean;
  emailOnWeeklyDigest?: boolean;
}

export interface CreatePersonRequest {
  username: string;
  firstName: string;
  lastName: string;
  displayName?: string | null;
  homeCity?: string | null;
}

export interface UpdatePersonRequest {
  displayName?: string | null;
  homeCity?: string | null;
  emailOnAbsenceReviewed?: boolean;
  emailOnNewAbsenceRequest?: boolean;
  emailOnWeeklyDigest?: boolean;
}

export interface AbsenceStatsResponse {
  year: number;
  vacationDays: number;
  otherDays: number;
}

export interface PersonAbsenceStatsDto {
  personId: number;
  year: number;
  vacationDays: number;
  otherDays: number;
}

export interface AllAbsenceStatsResponse {
  stats: PersonAbsenceStatsDto[];
}
