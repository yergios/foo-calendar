export interface PeopleResponse {
  people: PersonDto[];
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
