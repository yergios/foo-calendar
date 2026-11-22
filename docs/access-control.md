# Access control

Who can do what, how permissions are granted through your identity provider, and how to add new ones.

Signing in is enough to do everything a member of the team does. Five permissions gate the rest, each configured on its own and each enforced by a policy of the same name:

| Who | Can do |
| --------------------------- | ------------------------------------------------------------------------------------------ |
| Anyone signed in | View the calendar, manage their own profile and absences, add/edit/delete manual bank holidays |
| `PeopleManage` | Manage any person's profile, [import people in bulk](administration.md#importing-people-in-bulk) |
| `ReportsRead` | View team-wide absence totals |
| `HolidaysSync` | Trigger a bank-holiday sync by hand |
| `AuditRead` | Read the [audit log](administration.md#who-changed-what) and `/healthchecks/detail` |
| `AbsencesApprove` | Approve or reject pending absence requests, see [Absence approvals](administration.md#absence-approvals) |

Keeping the calendar's public holidays right is everyone's job, deliberately: a member can add, edit, and delete manual holidays without asking anyone. `HolidaysSync` is separate because it calls out to the holiday source and writes in bulk, not because a holiday is precious.

`AbsencesApprove` is the one exception to "every permission must be configured": it only does anything once `AbsenceApprovals:Enabled` turns the review workflow on, so a deployment that never asked for it is free to leave it unconfigured, see [Absence approvals](administration.md#absence-approvals).

`/admin` is a page of sections, each behind its own permission, so it shows whatever the signed-in user is allowed to see of it. Someone holding none of them never sees the nav item at all.

**Configuring who holds what.** `Auth:Permissions` maps each permission to the claim values that grant it. Values are lists because providers rarely hand you one tidy permission string:

```json
"Auth": {
  "Permissions": {
    "PeopleManage": [ "8f4c2f1a-0000-4c1e-9a3e-1c2d3e4f5a6b", "leave-admin" ],
    "ReportsRead": [ "leave-admin", "people-team" ],
    "HolidaysSync": [ "leave-admin", "office-coordinator" ],
    "AuditRead": [ "leave-admin" ]
  },
  "ClaimTypes": { "Permissions": [ "roles", "groups" ] }
}
```

- Every permission except `AbsencesApprove` must name at least one value or the app refuses to start; a name that isn't one of the five is rejected as a typo rather than silently granting nothing.
- Holding **any one** of a permission's values is enough, so a group id and a friendlier role name can coexist while you migrate between them.
- Nothing stops the same value appearing under all of them. That is how you configure one undivided admin role, and it is what the shipped `appsettings.json` does.
- `ClaimTypes:Permissions` names the claims to look in, searched in order. Entra ID, for instance, puts app roles in `roles` and group memberships in `groups`; listing both lets either grant a permission.
- Claim names are matched exactly (JWT claim names are case-sensitive), values case-insensitively — group ids are often GUIDs whose casing differs between a provider's dashboard and its tokens.

Grant the values to a user in your OIDC provider's dashboard. Locally, the dev bypass holds **every** value listed under `Auth:Permissions`, so the whole app is available with no identity provider at all. Set `Auth:DevUser:GrantAllPermissions` to `false` to try it as a plain member instead, or as whatever `Auth:DevUser:Permissions` lists.

The API's policies and the SPA's gating read the same map — served to the browser at startup — so they cannot disagree.

> Overriding a list through environment variables is index-based: `Auth__ClaimTypes__Permissions__0=roles`, `__1=groups`, `Auth__Permissions__PeopleManage__0=leave-admin`. Indexes you don't set keep whatever `appsettings.json` had at that position, so replacing a longer list with a shorter one means blanking the leftovers (`Auth__Permissions__PeopleManage__1=`). Blank entries are dropped rather than matched.

**Adding an endpoint.** Authorization is closed by default: a fallback policy requires an authenticated user, so a new route is protected without doing anything. A route needing a permission asks for it by name (`.RequireAuthorization(Permissions.PeopleManage)`), and the few genuinely public ones say so explicitly with `.AllowAnonymous()` — currently the SPA shell, `/api/config/bootstrap`, and `/healthchecks`.

**Adding a permission.** Add a constant to `Permissions.All` (`src/Api/Configuration/Permissions.cs`) and the matching name to `src/Web/src/types/permission.ts`. `Program.cs` registers a policy per entry, so nothing else needs touching — but every deployment must then configure it, since a permission nobody can hold fails startup rather than sitting there unreachable. Put it in `Permissions.Optional` instead if it gates a feature that is itself off by default (`AbsencesApprove` is the example): it still gets a policy and still has to be one of the recognized names, it just isn't required to be configured.
