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
        var authConfig = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            // Not RequireClaim: that pairs one claim name with its accepted values, and
            // this has to hold across several of each.
            .RequireAuthorization(p => p.RequireAssertion(ctx =>
                ctx.User.HasAnyPermission(authConfig.ClaimTypes.Permissions, authConfig.AdminPermissions)));

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
        IOptions<RegionCatalogOptions> regionCatalog,
        IOptions<HolidayOptions> holidayOptions,
        IBankHolidayService bankHolidays,
        CancellationToken ct)
    {
        // Nothing to sync from, so say so rather than reporting every country as synced.
        if (holidayOptions.Value.Source == HolidayOptions.SourceNone)
            throw new InvalidOperationException(
                "Holiday syncing is disabled (Holidays:Source=None).");

        var countries = regionCatalog.Value.Select(c => c.Code).ToList();
        var failed = await bankHolidays.SyncCountriesAsync(countries, year, ct);

        return Results.Ok(new SyncBankHolidaysResponse(
            [.. countries.Except(failed)],
            failed));
    }
}
