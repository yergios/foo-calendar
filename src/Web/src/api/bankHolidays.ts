import { apiFetch, apiJson } from "@/api";

import type {
  AddManualHolidayRequest,
  BankHolidayDto,
  BankHolidaysResponse,
} from "../types/bankHoliday";

export function getCatalogHolidays(from: string, to: string): Promise<BankHolidaysResponse> {
  return apiJson(`/api/bank-holidays/catalog?from=${from}&to=${to}`);
}

export function addManualHoliday(request: AddManualHolidayRequest): Promise<BankHolidayDto> {
  return apiJson(`/api/bank-holidays`, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateBankHoliday(
  id: number,
  request: AddManualHolidayRequest,
): Promise<BankHolidayDto> {
  return apiJson(`/api/bank-holidays/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export async function deleteBankHoliday(id: number): Promise<void> {
  await apiFetch(`/api/bank-holidays/${id}`, { method: "DELETE" });
}
