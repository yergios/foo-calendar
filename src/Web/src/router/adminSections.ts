import type { Component } from "vue";

import AdminApprovalsView from "@/views/admin/AdminApprovalsView.vue";
import AdminAuditView from "@/views/admin/AdminAuditView.vue";
import AdminBankHolidaysView from "@/views/admin/AdminBankHolidaysView.vue";
import AdminPeopleView from "@/views/admin/AdminPeopleView.vue";
import { Permission } from "@/types/permission";

export interface AdminSection {
  /** Appended to "/admin/" for the route and used as the sidebar link's key. */
  path: string;
  label: string;
  component: Component;
  /** Held if the signed-in user has any one of these - the People page, for instance,
   * is worth reaching with just ReportsRead even without PeopleManage. */
  permissions: Permission[];
}

// The single source of truth for what the admin area contains: the router builds
// /admin's child routes from this, AdminLayout builds its sidebar from it, and both stay
// in agreement about which permission unlocks which page because there's only one list.
export const ADMIN_SECTIONS: AdminSection[] = [
  {
    path: "people",
    label: "People",
    component: AdminPeopleView,
    permissions: [Permission.peopleManage, Permission.reportsRead],
  },
  {
    path: "bank-holidays",
    label: "Bank holidays",
    component: AdminBankHolidaysView,
    permissions: [Permission.holidaysSync],
  },
  {
    path: "approvals",
    label: "Absence approvals",
    component: AdminApprovalsView,
    permissions: [Permission.absencesApprove],
  },
  {
    path: "audit",
    label: "Audit log",
    component: AdminAuditView,
    permissions: [Permission.auditRead],
  },
];
