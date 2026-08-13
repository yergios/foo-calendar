using System.Security.Claims;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization(AuthOptions.AdminPolicy);

        group.MapPost("/people", CreatePerson);
        group.MapPost("/people/import", ImportPeople);
        group.MapPut("/people/{id:int}", UpdatePerson);
        group.MapDelete("/people/{id:int}", DeletePerson);
        group.MapGet("/people/stats", GetAllPeopleStats);
        group.MapPost("/bank-holidays/sync", SyncBankHolidays);
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

    private static async Task<IResult> SyncBankHolidays(
        int year,
        string region,
        IOptions<HolidayOptions> holidayOptions,
        ITeamRegionService teamRegions,
        IBankHolidayService bankHolidays,
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

        return Results.Ok(new SyncBankHolidaysResponse(
            [.. regions.Except(failed, StringComparer.Ordinal)],
            failed));
    }
}
