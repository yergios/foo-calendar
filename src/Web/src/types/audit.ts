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

/** Mirrors the API's AuditActions constants (Api/Data/Models/AuditEntry.cs) - the closed
 * set of actions the log can ever record, offered as the action-filter's options. Treat
 * renames as breaking, same warning as the source. */
export const AuditAction = {
  personCreated: "person.created",
  personUpdated: "person.updated",
  personDeleted: "person.deleted",
  peopleImported: "people.imported",
  absenceCreated: "absence.created",
  absenceUpdated: "absence.updated",
  absenceDeleted: "absence.deleted",
  // No longer recorded since approvals were removed; still offered as filters because the
  // log keeps the entries written before then until retention removes them.
  absenceApproved: "absence.approved",
  absenceRejected: "absence.rejected",
  bankHolidayCreated: "bank-holiday.created",
  bankHolidayUpdated: "bank-holiday.updated",
  bankHolidayDeleted: "bank-holiday.deleted",
  bankHolidaysSynced: "bank-holidays.synced",
} as const;

export type AuditAction = (typeof AuditAction)[keyof typeof AuditAction];

/** Matches the API's AuditSortColumn constants (Api/Services/AuditSortColumn.cs). */
export type AuditSortColumn = "occurredAt" | "actor" | "action";

export interface AuditLogQuery {
  /** Matches Actor as a substring. */
  search?: string;
  /** A plain string, not AuditAction: the filter select's v-model binds a bare string
   * (including "" for "all actions"), which is always one of AuditAction's values by
   * construction but isn't worth narrowing back for the one call site that sends it. */
  action?: string;
  /** ISO instants, not calendar dates: the caller converts a picked date range into the
   * right UTC bounds first, so filtering by "day" respects the viewer's own timezone. */
  from?: string;
  /** Exclusive upper bound - see `from`. */
  to?: string;
  sortBy?: AuditSortColumn;
  descending?: boolean;
}
