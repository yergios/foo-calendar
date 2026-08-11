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
        var group = app.MapGroup("/api/bank-holidays").RequireAuthorization();

        group.MapGet("/catalog", GetCatalogHolidays);
        group.MapPost("/", AddManual);
        group.MapPut("/{id:int}", UpdateManual);
        group.MapDelete("/{id:int}", DeleteManual);
    }

    private static async Task<IResult> GetCatalogHolidays(
        DateOnly from,
        DateOnly to,
        IOptions<RegionCatalogOptions> regionCatalog,
        IBankHolidayService bankHolidays,
        CancellationToken ct)
    {
        var regions = regionCatalog.Value
            .SelectMany(country => country.Regions.Select(r => r.Code))
            .Distinct()
            .ToList();

        var result = await bankHolidays.GetForRegionsAsync(regions, from, to, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> AddManual(
        AddManualHolidayRequest request,
        ClaimsPrincipal user,
        IBankHolidayService bankHolidays,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var created = await bankHolidays.AddManualAsync(request, user.GetUsername(authOptions.Value.ClaimTypes.UserName), ct);
        return Results.Created($"/api/bank-holidays/{created.Id}", created);
    }

    private static async Task<IResult> UpdateManual(
        int id,
        AddManualHolidayRequest request,
        ClaimsPrincipal user,
        IBankHolidayService bankHolidays,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var updated = await bankHolidays.UpdateManualAsync(id, request, user.GetUsername(authOptions.Value.ClaimTypes.UserName), ct);
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
