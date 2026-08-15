using Api.Dtos;
using Api.Interfaces;

namespace Api.Endpoints;

public static class AbsencesEndpoints
{
    public static void MapAbsencesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/absences").RequireAuthorization().WithTags("Absences");

        group.MapGet("/", GetAbsences);
        group.MapPost("/", CreateAbsence);
        group.MapPut("/{id:int}", UpdateAbsence);
        group.MapDelete("/{id:int}", DeleteAbsence);
    }

    private static async Task<IResult> GetAbsences(
        DateOnly from,
        DateOnly to,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        // FindAsync, not GetAsync: the calendar is readable from the first sign-in, before
        // anyone has set up a profile. Someone with none simply owns no notes.
        var viewer = await currentPerson.FindAsync(ct);

        var result = await absences.GetOverlappingAsync(from, to, viewer?.Id, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAbsence(
        CreateAbsenceRequest request,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var person = await currentPerson.GetAsync(ct);
        var created = await absences.CreateAsync(person, request, ct);
        return Results.Created($"/api/absences/{created.Id}", created);
    }

    private static async Task<IResult> UpdateAbsence(
        int id,
        CreateAbsenceRequest request,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var person = await currentPerson.GetAsync(ct);
        var updated = await absences.UpdateAsync(person, id, request, ct);
        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteAbsence(
        int id,
        ICurrentPersonAccessor currentPerson,
        IAbsenceService absences,
        CancellationToken ct)
    {
        var person = await currentPerson.GetAsync(ct);
        await absences.DeleteAsync(person, id, ct);
        return Results.NoContent();
    }
}
