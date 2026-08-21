using System.Security.Claims;

using Api.Configuration;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        // Gated per route rather than per group: these are four different jobs that happen
        // to share a path prefix, and a deployment shouldn't have to hand out the ability
        // to delete people to let someone re-run a failed holiday sync.
        var group = app.MapGroup("/api/admin").WithTags("Admin");

        group.MapPost("/people", CreatePerson).RequireAuthorization(Permissions.PeopleManage);
        group.MapPost("/people/import", ImportPeople).RequireAuthorization(Permissions.PeopleManage);
        group.MapPut("/people/{id:int}", UpdatePerson).RequireAuthorization(Permissions.PeopleManage);
        group.MapDelete("/people/{id:int}", DeletePerson).RequireAuthorization(Permissions.PeopleManage);
        group.MapGet("/people/stats", GetAllPeopleStats).RequireAuthorization(Permissions.ReportsRead);
        group.MapPost("/bank-holidays/sync", SyncBankHolidays).RequireAuthorization(Permissions.HolidaysSync);
        group.MapGet("/audit", GetAuditLog).RequireAuthorization(Permissions.AuditRead);
        group.MapGet("/absences/pending", GetPendingAbsences).RequireAuthorization(Permissions.AbsencesApprove);
        group.MapPost("/absences/{id:int}/approve", ApproveAbsence).RequireAuthorization(Permissions.AbsencesApprove);
        group.MapPost("/absences/{id:int}/reject", RejectAbsence).RequireAuthorization(Permissions.AbsencesApprove);
    }

    private static async Task<IResult> CreatePerson(
        CreatePersonRequest request,
        ClaimsPrincipal user,
        IPersonService people,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var created = await people.CreateAsync(request, user.GetUsername(authOptions.Value.ClaimTypes.UserName), ct);
        return Results.Created("/api/admin/people/me", created);
    }

    private static async Task<IResult> ImportPeople(
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        IPersonService people,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        // The raw body rather than a multipart upload: this is reached from a script or a
        // curl pipe far more often than from a file picker.
        using var reader = new StreamReader(httpRequest.Body);
        var csv = await reader.ReadToEndAsync(ct);

        var result = await people.ImportAsync(
            csv, user.GetUsername(authOptions.Value.ClaimTypes.UserName), ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> UpdatePerson(
        int id,
        UpdatePersonRequest request,
        ClaimsPrincipal user,
        IPersonService people,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var updated = await people.UpdateAsync(id, request, user.GetUsername(authOptions.Value.ClaimTypes.UserName), ct);
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeletePerson(
        int id,
        IPersonService people,
        CancellationToken ct)
    {
        await people.DeleteAsync(id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAllPeopleStats(
        int year,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var result = await absences.GetAllStatsAsync(year, ct);
        return Results.Ok(result);
    }

    /// <summary>Newest first by default, paged. <paramref name="from"/>/<paramref name="to"/>
    /// are exact instants, not calendar dates: the caller (the admin page) is the one that
    /// knows the viewer's own timezone, and converts a picked date range into the right UTC
    /// bounds before asking here.</summary>
    private static async Task<IResult> GetAuditLog(
        int? page,
        int? pageSize,
        string search,
        string action,
        DateTime? from,
        DateTime? to,
        string sortBy,
        string sortDir,
        IAuditLog audit,
        CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? DefaultAuditPageSize, 1, MaxAuditPageSize);

        var result = await audit.ReadAsync(
            size,
            (Math.Max(page ?? 1, 1) - 1) * size,
            search,
            action,
            from,
            to,
            sortBy ?? AuditSortColumn.OccurredAt,
            sortDir != "asc",
            ct);

        return Results.Ok(result);
    }

    private const int DefaultAuditPageSize = 50;
    private const int MaxAuditPageSize = 200;

    private static async Task<IResult> SyncBankHolidays(
        int year,
        string region,
        IOptions<HolidayOptions> holidayOptions,
        ITeamRegionService teamRegions,
        IBankHolidayService bankHolidays,
        IAuditLog audit,
        CancellationToken ct)
    {
        // Nothing to sync from, so say so rather than reporting every region as synced.
        if (holidayOptions.Value.Source == HolidayOptions.SourceNone)
            throw new InvalidOperationException(
                "Holiday syncing is disabled (Holidays:Source=None).");

        var tracked = await teamRegions.InUseAsync(ct);

        // Omitting the region retries everything. Naming one is for the case this exists
        // for: a single country's fetch failed on the scheduled run, and re-fetching every
        // other country to get at it would be rude to the source and slow for the admin.
        if (region is not null && !tracked.Contains(region, StringComparer.Ordinal))
            throw new ArgumentException(
                $"{region} is not one of the team's regions, so there is nothing to sync for it.");

        IReadOnlyCollection<string> regions = region is null ? tracked : [region];

        var failed = await bankHolidays.SyncRegionsAsync(regions, year, ct);
        var synced = regions.Except(failed, StringComparer.Ordinal).ToList();

        // Only the hand-triggered runs. The weekly job writing an entry every time would
        // bury the actions someone actually took, and its rows already say "<source>-sync".
        await audit.RecordAsync(
            AuditActions.BankHolidaysSynced,
            $"{year}, {(region ?? "all regions")}",
            $"{synced.Count} region(s) synced"
                + (failed.Count > 0 ? $", failed: {string.Join(", ", failed)}" : ""),
            ct);

        return Results.Ok(new SyncBankHolidaysResponse(synced, failed));
    }

    private static async Task<IResult> GetPendingAbsences(IAbsenceService absences, CancellationToken ct)
    {
        var result = await absences.GetPendingAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ApproveAbsence(
        int id,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var reviewer = await currentPerson.GetAsync(ct);
        var approved = await absences.ApproveAsync(reviewer, id, ct);
        return Results.Ok(approved);
    }

    private static async Task<IResult> RejectAbsence(
        int id,
        RejectAbsenceRequest request,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var reviewer = await currentPerson.GetAsync(ct);
        var rejected = await absences.RejectAsync(reviewer, id, request.Note, ct);
        return Results.Ok(rejected);
    }
}
