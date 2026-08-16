using Api.Configuration;
using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class AbsenceService(
    AppDbContext db,
    IWebhookPublisher webhooks,
    IAuditLog audit,
    INotificationService notifications,
    IOptions<AbsenceApprovalOptions> approvalOptions) : IAbsenceService
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
        var absences = await VisibleOverlapping(db.Absences, from, to)
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
        var overlaps = await VisibleOverlapping(
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
            // Approvals off (the default) keeps today's behavior: everything lands
            // Approved the instant it's created. On, it waits for a reviewer.
            Status = approvalOptions.Value.Enabled ? AbsenceStatus.Pending : AbsenceStatus.Approved,
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

        // Rejected is a dead end by design: it no longer shows on the calendar, and editing
        // it back to life would resurrect a request the reviewer already turned down.
        // Filing a new one is the only way forward from here.
        if (absence.Status == AbsenceStatus.Rejected)
            throw new InvalidOperationException(
                "This request was rejected and can no longer be edited. Submit a new one instead.");

        var overlaps = await VisibleOverlapping(
            db.Absences.Where(a => a.PersonId == person.Id && a.Id != absenceId),
            request.StartDate,
            request.EndDate)
            .AnyAsync(ct);

        if (overlaps)
            throw new InvalidOperationException("This absence overlaps one you already have.");

        // Editing an absence that was already approved sends it through review again: what
        // was approved was these specific dates, type and note, and any of the three could
        // have just changed. A still-Pending one simply stays Pending, nothing to reset.
        if (approvalOptions.Value.Enabled && absence.Status == AbsenceStatus.Approved)
        {
            absence.Status = AbsenceStatus.Pending;
            absence.ReviewedBy = null;
            absence.ReviewedAt = null;
            absence.ReviewNote = null;
        }

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

    public async Task<PendingAbsencesResponse> GetPendingAsync(CancellationToken ct = default)
    {
        EnsureApprovalsEnabled();

        var absences = await db.Absences
            .Where(a => a.Status == AbsenceStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        // Deciding whether to approve is the one situation this app asks someone other
        // than the author to read their note for, so unlike every other read path this
        // includes it unconditionally.
        return new([.. absences.Select(a => AbsenceDto.FromEntity(a, includeNote: true))]);
    }

    public async Task<AbsenceDto> ApproveAsync(Person reviewer, int absenceId, CancellationToken ct = default)
    {
        EnsureApprovalsEnabled();

        var absence = await db.Absences.Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.Id == absenceId, ct)
            ?? throw new KeyNotFoundException($"Absence {absenceId} not found.");

        if (absence.Status != AbsenceStatus.Pending)
            throw new InvalidOperationException("Only a pending request can be approved.");

        absence.Status = AbsenceStatus.Approved;
        absence.ReviewedBy = reviewer.Username;
        absence.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            AuditActions.AbsenceApproved,
            absence.Person.DisplayName,
            $"{absence.Type}, {absence.StartDate:yyyy-MM-dd} → {absence.EndDate:yyyy-MM-dd}",
            ct);

        await notifications.NotifyAsync(absence, NotificationType.AbsenceApproved, note: null, ct);

        PublishEvent(WebhookEvents.AbsenceApproved, absence, absence.Person);

        return AbsenceDto.FromEntity(absence, includeNote: absence.PersonId == reviewer.Id);
    }

    public async Task<AbsenceDto> RejectAsync(
        Person reviewer, int absenceId, string note, CancellationToken ct = default)
    {
        EnsureApprovalsEnabled();

        var absence = await db.Absences.Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.Id == absenceId, ct)
            ?? throw new KeyNotFoundException($"Absence {absenceId} not found.");

        if (absence.Status != AbsenceStatus.Pending)
            throw new InvalidOperationException("Only a pending request can be rejected.");

        absence.Status = AbsenceStatus.Rejected;
        absence.ReviewedBy = reviewer.Username;
        absence.ReviewedAt = DateTime.UtcNow;
        absence.ReviewNote = note;
        await db.SaveChangesAsync(ct);

        // Dates and type, same as a deletion: never the reviewer's reason either, which
        // reaches the requester through their notification instead, not through a log
        // other people can read.
        await audit.RecordAsync(
            AuditActions.AbsenceRejected,
            absence.Person.DisplayName,
            $"{absence.Type}, {absence.StartDate:yyyy-MM-dd} → {absence.EndDate:yyyy-MM-dd}",
            ct);

        await notifications.NotifyAsync(absence, NotificationType.AbsenceRejected, note, ct);

        PublishEvent(WebhookEvents.AbsenceRejected, absence, absence.Person);

        return AbsenceDto.FromEntity(absence, includeNote: absence.PersonId == reviewer.Id);
    }

    private void EnsureApprovalsEnabled()
    {
        if (!approvalOptions.Value.Enabled)
            throw new InvalidOperationException(
                "Absence approvals are not enabled for this deployment (AbsenceApprovals:Enabled=false).");
    }

    // Approved and Pending both belong on the calendar and both block a new request over
    // the same dates; only Rejected is excluded, since a request that's been turned down
    // shouldn't reserve dates or block a fresh attempt at them.
    private static IQueryable<Absence> VisibleOverlapping(IQueryable<Absence> query, DateOnly from, DateOnly to) =>
        query.Where(a => a.Status != AbsenceStatus.Rejected && a.StartDate <= to && a.EndDate >= from);

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
