/**
 * The permissions the API defines, by the exact names it configures and enforces them
 * under (see Api/Configuration/Permissions.cs). Signing in already covers everything a
 * member does: their own profile, their own absences, and the team's manually added bank
 * holidays. These are the rest.
 */
export const Permission = {
  peopleManage: "PeopleManage",
  reportsRead: "ReportsRead",
  holidaysSync: "HolidaysSync",
  auditRead: "AuditRead",
  absencesApprove: "AbsencesApprove",
} as const;

export type Permission = (typeof Permission)[keyof typeof Permission];
