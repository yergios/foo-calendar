using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class AbsenceService(
    AppDbContext db, IWebhookPublisher webhooks, IAuditLog audit) : IAbsenceService
{
    // Published after the change is committed, so a receiver can never be told about an
    // absence that then failed to save.
    private void PublishEvent(string name, Absence absence, Person person) =>
        webhooks.Publish(new WebhookEvent(
            Guid.NewGuid().ToString(),
            name,
            DateTime.UtcNow,
            person.Username,
            WebhookAbsence.FromEntity(absence, person.DisplayName)));

    public async Task<AbsencesResponse> GetOverlappingAsync(
        DateOnly from, DateOnly to, int? viewerPersonId, CancellationToken ct = default)
    {
        var absences = await ApprovedOverlapping(db.Absences, from, to)
            .OrderBy(a => a.StartDate)
            .ToListAsync(ct);

        // Notes come back only on the viewer's own rows. Filtered here rather than in the
        // client so a note never leaves the process, whoever is asking and however they ask.
        return new([.. absences.Select(a => AbsenceDto.FromEntity(a, a.PersonId == viewerPersonId))]);
    }

    public async Task<AbsenceStatsResponse> GetStatsAsync(int personId, int year, CancellationToken ct = default)
    {
        var (yearStart, yearEnd) = YearRange(year);

        var absences = await ApprovedOverlapping(db.Absences.Where(a => a.PersonId == personId), yearStart, yearEnd)
            .ToListAsync(ct);

        return ComputeStats(absences, year, yearStart, yearEnd);
    }

    public async Task<AllAbsenceStatsResponse> GetAllStatsAsync(int year, CancellationToken ct = default)
    {
        var (yearStart, yearEnd) = YearRange(year);

        var personIds = await db.People.Select(p => p.Id).ToListAsync(ct);
        var absences = await ApprovedOverlapping(db.Absences, yearStart, yearEnd).ToListAsync(ct);
        var byPerson = absences.ToLookup(a => a.PersonId);

        var stats = personIds
            .Select(id =>
            {
                var personStats = ComputeStats(byPerson[id], year, yearStart, yearEnd);
                return new PersonAbsenceStatsResponse(
                    id, year, personStats.VacationDays, personStats.SickDays, personStats.OtherDays);
            })
            .ToList();

        return new AllAbsenceStatsResponse(stats);
    }

    public async Task<AbsenceDto> CreateAsync(Person person, CreateAbsenceRequest request, CancellationToken ct = default)
    {
        var overlaps = await ApprovedOverlapping(
            db.Absences.Where(a => a.PersonId == person.Id),
            request.StartDate,
            request.EndDate)
            .AnyAsync(ct);

        if (overlaps)
            throw new InvalidOperationException("This absence overlaps one you already have.");

        var absence = new Absence
        {
            PersonId = person.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Type = request.Type,
            Status = AbsenceStatus.Approved, // hardcoded for now
            Note = request.Note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = person.Username,
        };

        db.Absences.Add(absence);
        await db.SaveChangesAsync(ct);

        PublishEvent(WebhookEvents.AbsenceCreated, absence, person);

        // Their own absence coming back to them, so the note goes with it.
        return AbsenceDto.FromEntity(absence, includeNote: true);
    }

    public async Task<AbsenceDto> UpdateAsync(
        Person person, int absenceId, CreateAbsenceRequest request, CancellationToken ct = default)
    {
        var absence = await db.Absences.FindAsync([absenceId], ct)
            ?? throw new KeyNotFoundException($"Absence {absenceId} not found.");

        if (absence.PersonId != person.Id)
            throw new UnauthorizedAccessException("Cannot edit another person's absence.");

        var overlaps = await ApprovedOverlapping(
            db.Absences.Where(a => a.PersonId == person.Id && a.Id != absenceId),
            request.StartDate,
            request.EndDate)
            .AnyAsync(ct);

        if (overlaps)
            throw new InvalidOperationException("This absence overlaps one you already have.");

        absence.StartDate = request.StartDate;
        absence.EndDate = request.EndDate;
        absence.Type = request.Type;
        absence.Note = request.Note;
        absence.UpdatedAt = DateTime.UtcNow;
        absence.UpdatedBy = person.Username;
        await db.SaveChangesAsync(ct);

        PublishEvent(WebhookEvents.AbsenceUpdated, absence, person);
        return AbsenceDto.FromEntity(absence, includeNote: true);
    }

    public async Task DeleteAsync(Person person, int absenceId, CancellationToken ct = default)
    {
        var absence = await db.Absences.FindAsync([absenceId], ct)
            ?? throw new KeyNotFoundException($"Absence {absenceId} not found.");

        if (absence.PersonId != person.Id)
            throw new UnauthorizedAccessException("Cannot delete another person's absence.");

        db.Absences.Remove(absence);
        await db.SaveChangesAsync(ct);

        // Dates and type, never the note: the log is read by people the note was never
        // meant for, and a deleted absence shouldn't outlive its author's privacy.
        await audit.RecordAsync(
            AuditActions.AbsenceDeleted,
            person.DisplayName,
            $"{absence.Type}, {absence.StartDate:yyyy-MM-dd} → {absence.EndDate:yyyy-MM-dd}",
            ct);

        // Describes the absence as it was, since there is nothing left to look up.
        PublishEvent(WebhookEvents.AbsenceDeleted, absence, person);
    }

    private static IQueryable<Absence> ApprovedOverlapping(IQueryable<Absence> query, DateOnly from, DateOnly to) =>
        query.Where(a => a.Status == AbsenceStatus.Approved && a.StartDate <= to && a.EndDate >= from);

    private static (DateOnly Start, DateOnly End) YearRange(int year) =>
        (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    private static AbsenceStatsResponse ComputeStats(
        IEnumerable<Absence> absences, int year, DateOnly yearStart, DateOnly yearEnd)
    {
        var vacationDays = 0;
        var sickDays = 0;
        var otherDays = 0;

        foreach (var absence in absences)
        {
            var days = CountWorkingDays(Max(absence.StartDate, yearStart), Min(absence.EndDate, yearEnd));
            switch (absence.Type)
            {
                case AbsenceType.Vacation: vacationDays += days; break;
                case AbsenceType.Sick: sickDays += days; break;
                case AbsenceType.Other: otherDays += days; break;
            }
        }

        return new AbsenceStatsResponse(year, vacationDays, sickDays, otherDays);
    }

    private static int CountWorkingDays(DateOnly start, DateOnly end)
    {
        var count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                count++;
        return count;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
