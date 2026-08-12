// Mirrors the API's HolidaySource enum. Anything other than "Manual" was put there by the
// configured holiday source (see Holidays:Source) and is refreshed on every sync.
export type HolidaySource = "Manual" | "Nager" | "File";

export interface BankHolidayDto {
  id: number;
  region: string;
  date: string; // yyyy-MM-dd
  name: string;
  source: HolidaySource;
  isRecurringAnnually: boolean;
}

export interface BankHolidaysResponse {
  holidays: BankHolidayDto[];
}

export interface AddManualHolidayRequest {
  region: string;
  date: string; // yyyy-MM-dd
  name: string;
  isRecurringAnnually: boolean;
}

export interface SyncBankHolidaysResponse {
  syncedCountries: string[];
  failedCountries: string[];
}
