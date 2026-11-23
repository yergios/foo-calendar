using System.Security.Claims;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public static class PeopleEndpoints
{
    public static void MapPeopleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/people").RequireAuthorization().WithTags("People");

        group.MapPost("/", CreateMe);
        group.MapGet("/", GetAll);
        group.MapGet("/paged", GetPaged);
        group.MapGet("/me", GetMe);
        group.MapPut("/me", UpdateMe);
        group.MapGet("/me/stats", GetMyStats);
        group.MapDelete("/me", DeleteMe);
    }

    private static async Task<IResult> CreateMe(
        UpdatePersonRequest request,
        ClaimsPrincipal user,
        IPersonService people,
        IOptions<AuthOptions> authOptions,
        CancellationToken ct)
    {
        var claimTypes = authOptions.Value.ClaimTypes;
        var username = user.GetUsername(claimTypes.UserName);

        var create = new CreatePersonRequest(
            username,
            user.FindFirstValue(claimTypes.FirstName),
            user.FindFirstValue(claimTypes.LastName),
            request.DisplayName,
            request.HomeCity);

        var created = await people.CreateAsync(create, username, ct);
        return Results.Created("/api/people/me", created);
    }

    private static async Task<IResult> GetAll(IPersonService people, CancellationToken ct)
    {
        var result = await people.GetAllAsync(ct);
        return Results.Ok(result);
    }

    private const int DefaultPeoplePageSize = 25;
    private const int MaxPeoplePageSize = 200;

    // Same base authorization as GetAll above (just RequireAuthorization on the group, no
    // specific permission): the admin people page is reachable with either PeopleManage or
    // ReportsRead (see ADMIN_SECTIONS on the frontend), and search/region-filter/paging
    // return nothing GetAll doesn't already hand any signed-in user today. Sorting by a
    // stats column is the one exception - see the ReportsRead check below.
    private static async Task<IResult> GetPaged(
        int? page,
        int? pageSize,
        string? search,
        string[]? regions,
        string? sortBy,
        string? sortDir,
        int? year,
        ClaimsPrincipal user,
        IOptions<AuthOptions> authOptions,
        IPersonService people,
        CancellationToken ct)
    {
        if (year is not null)
            RequestYear.EnsureValid(year.Value);

        var sort = sortBy ?? PersonSortColumn.Name;

        // Vacation/other are the same per-year numbers the ReportsRead-gated stats
        // endpoint hands out; sorting by one would otherwise let PeopleManage alone infer
        // them a page at a time.
        if (PersonSortColumn.StatsColumns.Contains(sort) && !user.HasAnyPermission(
                authOptions.Value.ClaimTypes.Permissions, authOptions.Value.ValuesFor(Permissions.ReportsRead)))
        {
            return Results.Forbid();
        }

        var result = await people.GetPageAsync(
            Math.Max(page ?? 1, 1),
            Math.Clamp(pageSize ?? DefaultPeoplePageSize, 1, MaxPeoplePageSize),
            search,
            regions,
            sort,
            sortDir == "desc",
            year,
            ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetMe(
        ICurrentPersonAccessor currentPerson,
        CancellationToken ct)
    {
        var person = await currentPerson.GetAsync(ct);
        return Results.Ok(person);
    }

    private static async Task<IResult> UpdateMe(
        UpdatePersonRequest request,
        ICurrentPersonAccessor currentPerson,
        IPersonService people,
        CancellationToken ct)
    {
        var person = await currentPerson.GetAsync(ct);
        var updated = await people.UpdateAsync(person.Id, request, person.Username, ct);
        return Results.Ok(updated);
    }

    private static async Task<IResult> GetMyStats(
        int? year,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var targetYear = year ?? DateTime.UtcNow.Year; // This will be wrong only ~1hr/year at midnight
        RequestYear.EnsureValid(targetYear);

        var person = await currentPerson.GetAsync(ct);

        var result = await absences.GetStatsAsync(person, targetYear, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteMe(
        ICurrentPersonAccessor currentPerson,
        IPersonService people,
        CancellationToken ct)
    {
        var person = await currentPerson.GetAsync(ct);
        await people.DeleteAsync(person.Id, ct);
        return Results.NoContent();
    }
}
