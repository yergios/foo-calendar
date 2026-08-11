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
            .RequireAuthorization(p => p.RequireClaim(authConfig.ClaimTypes.Permissions, authConfig.AdminPermission));

        group.MapPost("/people", CreatePerson);
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
        IBankHolidayService bankHolidays,
        CancellationToken ct)
    {
        var countries = regionCatalog.Value.Select(c => c.Code).ToList();
        var failed = await bankHolidays.SyncCountriesAsync(countries, year, ct);

        return Results.Ok(new SyncBankHolidaysResponse(
            [.. countries.Except(failed)],
            failed));
    }
}
