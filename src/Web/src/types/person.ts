export interface PeopleResponse {
  people: PersonDto[];
  /** Display names for every region the team occupies and the countries they sit in.
   * Sent with the people because it changes with them, unlike anything in AppConfig. */
  regionLabels: Record<string, string>;
}

export interface PersonDto {
  id: number;
  username: string;
  displayName: string;
  homeCity: string | null;
  color: string;
}

export interface CreatePersonRequest {
  username: string;
  firstName: string;
  lastName: string;
  displayName?: string | null;
  homeCity?: string | null;
  color: string;
}

export interface UpdatePersonRequest {
  displayName?: string | null;
  homeCity?: string | null;
  color: string;
}

export interface AbsenceStatsResponse {
  year: number;
  vacationDays: number;
  sickDays: number;
  otherDays: number;
}

export interface ColorsInUseResponse {
  colors: string[];
}

export interface PersonAbsenceStatsDto {
  personId: number;
  year: number;
  vacationDays: number;
  sickDays: number;
  otherDays: number;
}

export interface AllAbsenceStatsResponse {
  stats: PersonAbsenceStatsDto[];
}
