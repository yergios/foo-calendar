# 0003. Nullable types and domain errors

- Status: Done
- Date: 2026-11-23
- Requirements added or changed: ERR-1, ERR-2, CFG-2 (`Auth:Authority` must be https),
  BLD-6 (see [SPEC.md](../../SPEC.md))

## Goal

Let the compiler and the type system carry two rules that today live in convention:

- which values may be null;
- which failures are the domain's answer to a bad request, as opposed to a bug.

0002's tests are the safety net for both refactors. A refactor that needs a test changed is
suspect: say why in the commit.

## Non-goals

- Changing any behaviour [SPEC.md](../../SPEC.md) describes, other than the three
  requirements above.
- A result-type or railway style instead of exceptions. Exceptions stay; they become types
  the handler can tell apart.
- Changing the database schema.

## Changes

### A. `Auth:Authority` must be https (CFG-2)

- Validate that `Auth:Authority` is an absolute URL whose scheme is `https`, with `http`
  accepted only in Development (a local Keycloak, for example). The message names the
  setting.
- Tests cover https accepted, http refused in Production, http accepted in Development, and
  a relative value refused.

### B. Domain errors (ERR-1, ERR-2)

- Four exception types the domain throws on purpose, under one base type:
  - invalid input (400);
  - acting on what the caller does not own (403);
  - not found (404);
  - conflict with what is stored (409).
- Replace every `throw` of `ArgumentException`, `UnauthorizedAccessException`,
  `KeyNotFoundException` and `InvalidOperationException` that expresses a domain rule (about
  35 in `src/Api`) with the matching type and the same message. Leave the framework and
  startup ones alone. Startup misconfiguration keeps throwing `InvalidOperationException`:
  it is not a request.
- `DomainExceptionHandler` maps only the base type. Every other exception falls through to
  the default handler.
- Configure problem details so that outside Development a 500 carries no exception message,
  type or stack. The exception is still logged.
- Tests:
  - each type maps to its status and keeps its message (ERR-1);
  - an endpoint that throws a plain `InvalidOperationException`, registered only in the
    test, answers 500 with no detail in Production, and the log holds the exception (ERR-2).

### C. Nullable reference types (BLD-6)

- Set `<Nullable>enable</Nullable>` in `Directory.Build.props`, so it covers the API, both
  test projects and the seed tool, and fix every warning. Warnings are errors, so the build
  is the check.
- Annotate what may be null as nullable, and what may not as non-nullable or `required`:
  entities, DTOs, options, services, and the SPA-facing records.
- Entities (amended 2026-11-23, see below): the schema stays as it is. A property the domain
  requires is non-nullable, `required` where it is set at creation, and mapped
  `.IsRequired(false)` where its column is nullable; a genuinely optional one is nullable.
  Required: `Person.Username`, every `CreatedBy`, `BankHoliday.Region`, `AuditEntry.Actor`
  and `AuditEntry.Action`. Optional: `Person.FirstName`, `LastName`, `HomeCity` and
  `DisplayName` (the API and SPA fall back to the username), every `UpdatedBy`,
  `Absence.Note`, `AuditEntry.Target` and `Details`, `BankHoliday.Name`. After the change,
  `dotnet ef migrations has-pending-model-changes` must report no changes, so the schema is
  untouched.
- Generated migrations keep the `#nullable disable` EF writes into them. Anywhere else,
  `!` and `#nullable disable` need a comment saying why the value cannot be null there.
- Remove the `CA1062` suppression's nullable remark from `.editorconfig` if the reasoning
  changes, and keep the suppression only if it still applies.
- Known defect this must close (found in the 0002 review fixes, 2026-11-23): with nullable
  off, minimal APIs treat a request body as optional, so `POST /api/people` with no body
  reaches the handler with `null` and answers 500. Once body parameters are non-nullable the
  framework answers 400. Add a test that every body-taking API route answers 400, not 500,
  to a member sending no body (ERR-1). Then tighten `RouteAccessTests`'
  `AMemberWithNoPermissions_...` check from "not 401/403" to "not 401/403 and below 500",
  as the permission-route checks already are.

### Amendment, 2026-11-23

§C first said both "what may not be null as non-nullable or `required`" and "a nullable
column stays nullable in C#". Those disagree for every string column, all of which allow
NULL, and the build followed the second: every entity string was `string?`, weaker than the
domain. That hid a defect: `POST /api/admin/people` without a username created a person
with none (201), and the next such request was a 409. EF Core maps a non-nullable property
to a nullable column with `.IsRequired(false)`, which leaves the model, and so the schema,
unchanged, so the entity bullet above now says which properties are required. The review
fixes that made the types match it also made `CreatePersonRequest.Username` required (400
when missing or blank).

## Order of work

Each step is its own commit and leaves CI green.

1. A.
2. B, the types and the handler first, then the throws service by service.
3. C, project by project: the API first, then the tests and the seed tool. If one commit
   per project is too large, go namespace by namespace with `#nullable enable` at file
   level, and finish by moving it into `Directory.Build.props`.
4. Remove ERR-1 and ERR-2 from the gap list in the commits that add their tests.

## Done when

- `dotnet build --no-incremental` has 0 warnings with nullable enabled everywhere, and
  `dotnet test` passes.
- `git grep -n "#nullable disable" -- src tests tools` finds only generated migrations.
- `git grep -n "!\\." -- src tests tools`, and every other null-forgiving use, finds each
  with a comment saying why it cannot be null.
- `dotnet ef migrations has-pending-model-changes --project src/Api` reports no changes.
- `git grep -n -E "throw new (ArgumentException|UnauthorizedAccessException|KeyNotFoundException)" -- src/Api`
  finds no domain rule.
- `node scripts/check-spec-trace.mjs` passes, with ERR-1 and ERR-2 off the gap list.
- CI is green on `master`.

## Open questions

None.
