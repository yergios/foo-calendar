export type HolidaySource = "Manual" | "Nager";

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
