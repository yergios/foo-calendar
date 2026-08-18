using System.Security.Claims;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public static class PeopleEndpoints
{
    public static void MapPeopleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/people").RequireAuthorization().WithTags("People");

        group.MapPost("/", CreateMe);
        group.MapGet("/", GetAll);
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
        var create = new CreatePersonRequest(
            user.FindFirstValue(claimTypes.UserName),
            user.FindFirstValue(claimTypes.FirstName),
            user.FindFirstValue(claimTypes.LastName),
            request.DisplayName,
            request.HomeCity,
            user.FindFirstValue(claimTypes.Email),
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
