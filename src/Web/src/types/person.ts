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
  sickDays: number;
  otherDays: number;
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
