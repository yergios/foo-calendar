import type { Component } from "vue";

import { Permission } from "@/types/permission";

export interface AdminSection {
  /** Appended to "/admin/" for the route and used as the sidebar link's key. */
  path: string;
  label: string;
  /** Lazy loaders so the admin views (and whatever only they import) stay out of the
   * main chunk - the calendar is the critical path and most sessions never open admin.
   * vue-router accepts a loader wherever it accepts a component. */
  component: Component | (() => Promise<{ default: Component }>);
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
    component: () => import("@/views/admin/AdminPeopleView.vue"),
    permissions: [Permission.peopleManage, Permission.reportsRead],
  },
  {
    path: "bank-holidays",
    label: "Bank holidays",
    component: () => import("@/views/admin/AdminBankHolidaysView.vue"),
    permissions: [Permission.holidaysSync],
  },
  {
    path: "audit",
    label: "Audit log",
    component: () => import("@/views/admin/AdminAuditView.vue"),
    permissions: [Permission.auditRead],
  },
];
