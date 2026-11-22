using System.Security.Claims;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public static class BankHolidaysEndpoints
{
    public static void MapBankHolidaysEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bank-holidays").RequireAuthorization().WithTags("Bank holidays");

        group.MapGet("/catalog", GetCatalogHolidays);
        group.MapPost("/", AddManual);
        group.MapPut("/{id:int}", UpdateManual);
        group.MapDelete("/{id:int}", DeleteManual);
    }

    private static async Task<IResult> GetCatalogHolidays(
        DateOnly from,
        DateOnly to,
        ITeamRegionService teamRegions,
        IBankHolidayService bankHolidays,
        CancellationToken ct)
    {
        ReadRange.EnsureValid(from, to);

        // Where the team actually is, which is also exactly what got synced. Read scope and
        // write scope come from the same place on purpose, so neither can drift into
        // showing holidays nobody stored or storing holidays nobody sees.
        var regions = await teamRegions.InUseAsync(ct);

        var result = await bankHolidays.GetForRegionsAsync(regions, from, to, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> AddManual(
        AddManualHolidayRequest request,
        ClaimsPrincipal user,
        ITeamRegionService teamRegions,
        IBankHolidayService bankHolidays,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var tracked = await teamRegions.InUseAsync(ct);
        var created = await bankHolidays.AddManualAsync(
            request, user.GetUsername(authOptions.Value.ClaimTypes.UserName), tracked, ct);

        return Results.Created($"/api/bank-holidays/{created.Id}", created);
    }

    private static async Task<IResult> UpdateManual(
        int id,
        AddManualHolidayRequest request,
        ClaimsPrincipal user,
        ITeamRegionService teamRegions,
        IBankHolidayService bankHolidays,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var tracked = await teamRegions.InUseAsync(ct);
        var updated = await bankHolidays.UpdateManualAsync(
            id, request, user.GetUsername(authOptions.Value.ClaimTypes.UserName), tracked, ct);

        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteManual(
        int id,
        IBankHolidayService bankHolidays,
        CancellationToken ct)
    {
        await bankHolidays.DeleteManualAsync(id, ct);
        return Results.NoContent();
    }
}
