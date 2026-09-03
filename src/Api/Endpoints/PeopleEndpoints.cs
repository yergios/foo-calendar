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

        // Null is "not supplied", so the identity provider's claim stands in; anything else
        // is what they typed, with an emptied box meaning they deliberately have no address.
        var email = request.EditsEmail
            ? request.ResolvedEmail
            : user.FindFirstValue(claimTypes.Email);

        var create = new CreatePersonRequest(
            user.FindFirstValue(claimTypes.UserName),
            user.FindFirstValue(claimTypes.FirstName),
            user.FindFirstValue(claimTypes.LastName),
            request.DisplayName,
            request.HomeCity,
            email,
            request.EmailOnAbsenceReviewed,
            request.EmailOnNewAbsenceRequest,
            request.EmailOnWeeklyDigest);

        var created = await people.CreateAsync(create, create.Username, ct);
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
        string search,
        string[] regions,
        string sortBy,
        string sortDir,
        int? year,
        ClaimsPrincipal user,
        IOptions<AuthOptions> authOptions,
        IPersonService people,
        CancellationToken ct)
    {
        var sort = sortBy ?? PersonSortColumn.Name;

        // Vacation/sick/other are the same per-year numbers the ReportsRead-gated stats
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
        var person = await currentPerson.GetAsync(ct);
        var targetYear = year ?? DateTime.UtcNow.Year; // This will be wrong only ~1hr/year at midnight

        var result = await absences.GetStatsAsync(person.Id, targetYear, ct);
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
