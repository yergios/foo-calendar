/** Mirrors the API's AuditEntryDto. */
export interface AuditEntryDto {
  id: number;
  occurredAt: string;
  actor: string;
  action: string;
  target: string;
  details: string | null;
}

/** Mirrors the API's AuditEntriesResponse. `total` is the whole log, not this page. */
export interface AuditEntriesResponse {
  entries: AuditEntryDto[];
  total: number;
}
