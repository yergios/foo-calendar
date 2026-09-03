using System.Data;

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
    IBankHolidayService bankHolidays,
    IAuditLog audit,
    INotificationService notifications,
    IOptions<AbsenceApprovalOptions> approvalOptions) : IAbsenceService
{
    // What an absence was, for an audit entry that still reads sensibly. Named to match
    // PersonService.Describe / BankHolidayService.Describe. A time-based absence is a
    // single day, so a date range reads oddly for it; its time range takes that spot
    // instead.
    private static string Describe(Absence absence) =>
        absence.StartTime is not null
            ? $"{absence.Type}, {absence.StartDate:yyyy-MM-dd} {absence.StartTime:HH\\:mm}-{absence.EndTime:HH\\:mm}"
            : $"{absence.Type}, {absence.StartDate:yyyy-MM-dd} → {absence.EndDate:yyyy-MM-dd}";

    public async Task<AbsencesResponse> GetOverlappingAsync(
        DateOnly from, DateOnly to, int? viewerPersonId, CancellationToken ct = default)
    {
        // ThenBy Id: a plain OrderBy(StartDate) leaves same-day rows in no guaranteed
        // order, which the calendar grid depends on being stable (it picks the first
        // same-day time-based absence's type to letter a stacked cell).
        //
        // Projected straight into the DTO rather than materialized as tracked entities and
        // mapped afterwards: this is the largest read in the app (a year for the whole
        // team), and nothing here is edited, so tracking and the second pass over every
        // row were pure cost. The projection also applies the notes rule (see
        // AbsenceDto.Projection), so a note never leaves the database.
        var absences = await VisibleOverlapping(db.Absences, from, to)
            .AsNoTracking()
            .OrderBy(a => a.StartDate)
            .ThenBy(a => a.Id)
            .Select(AbsenceDto.Projection(viewerPersonId))
            .ToListAsync(ct);

        return new(absences);
    }

    public async Task<AbsenceStatsResponse> GetStatsAsync(Person person, int year, CancellationToken ct = default)
    {
        var (yearStart, yearEnd) = YearRange(year);

        var absences = await ApprovedOverlapping(db.Absences.Where(a => a.PersonId == person.Id), yearStart, yearEnd)
            .ToListAsync(ct);

        var holidayDates = await HolidayDatesForAsync(person.HomeCity, yearStart, yearEnd, ct);

        return ComputeStats(absences, year, yearStart, yearEnd, holidayDates);
    }

    public async Task<AllAbsenceStatsResponse> GetAllStatsAsync(int year, CancellationToken ct = default)
    {
        var (yearStart, yearEnd) = YearRange(year);

        var people = await db.People.Select(p => new { p.Id, p.HomeCity }).ToListAsync(ct);
        var absences = await ApprovedOverlapping(db.Absences, yearStart, yearEnd).ToListAsync(ct);
        var byPerson = absences.ToLookup(a => a.PersonId);

        // One fetch covering every region anyone's home city touches, rather than one round
        // trip per person: a team's home regions are few, however many people share them.
        var regions = people.Select(p => p.HomeCity).Where(c => c is not null).Distinct().ToList();
        var holidays = regions.Count > 0
            ? (await bankHolidays.GetForRegionsAsync(regions!, yearStart, yearEnd, ct)).Holidays
            : [];

        var stats = people
            .Select(p =>
            {
                var holidayDates = DatesApplyingTo(p.HomeCity, holidays);
                var personStats = ComputeStats(byPerson[p.Id], year, yearStart, yearEnd, holidayDates);
                return new PersonAbsenceStatsResponse(
                    p.Id, year, personStats.VacationDays, personStats.OtherDays);
            })
            .ToList();

        return new AllAbsenceStatsResponse(stats);
    }

    // GetStatsAsync's own fetch, scoped to one person: GetForRegionsAsync already expands
    // a region to itself plus its country, so this comes back holding exactly the holidays
    // that count as this person's own.
    private async Task<HashSet<DateOnly>> HolidayDatesForAsync(
        string homeCity, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (homeCity is null) return [];
        var response = await bankHolidays.GetForRegionsAsync([homeCity], from, to, ct);
        return response.Holidays.Select(h => h.Date).ToHashSet();
    }

    // GetAllStatsAsync's batch fetch covers every region in play at once, so a holiday
    // belonging to one person's country sits alongside another's; this is what picks back
    // out just the ones that are actually this person's own, the same rule
    // RegionCode.BelongsTo enforces everywhere else a region has to match a person's.
    private static HashSet<DateOnly> DatesApplyingTo(string homeCity, IEnumerable<BankHolidayDto> holidays) =>
        homeCity is null
            ? []
            : holidays.Where(h => RegionCode.BelongsTo(homeCity, h.Region)).Select(h => h.Date).ToHashSet();

    public async Task<AbsenceDto> CreateAsync(Person person, CreateAbsenceRequest request, CancellationToken ct = default)
    {
        var absence = new Absence
        {
            PersonId = person.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Type = request.Type,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            // Approvals off (the default) keeps today's behavior: everything lands
            // Approved the instant it's created. On, it waits for a reviewer.
            Status = approvalOptions.Value.Enabled ? AbsenceStatus.Pending : AbsenceStatus.Approved,
            Note = request.Note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = person.Username,
        };

        // Serializable: SQLite takes the write lock the instant this transaction opens, so a
        // second, overlapping create just waits for this one to commit before running its own
        // overlap check, instead of both checks racing against a still-empty table.
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var overlaps = await OverlapCandidates(
                db.Absences.Where(a => a.PersonId == person.Id),
                request.StartTime is not null,
                request.StartDate,
                request.EndDate)
                .AnyAsync(ct);

            if (overlaps)
                throw new InvalidOperationException("This absence overlaps one you already have.");

            db.Absences.Add(absence);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // Dates and type, never the note: same reasoning as DeleteAsync.
        await audit.RecordAsync(AuditActions.AbsenceCreated, person.DisplayName, Describe(absence), ct);

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

        // Editing an absence that was already approved sends it through review again: what
        // was approved was these specific dates, type and note, and any of the three could
        // have just changed. A still-Pending one simply stays Pending, nothing to reset.
        var justBecamePending = approvalOptions.Value.Enabled && absence.Status == AbsenceStatus.Approved;

        // Serializable, same reasoning as CreateAsync: guards the overlap check against a
        // concurrent create/update landing an overlapping absence before this one commits.
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var overlaps = await OverlapCandidates(
                db.Absences.Where(a => a.PersonId == person.Id && a.Id != absenceId),
                request.StartTime is not null,
                request.StartDate,
                request.EndDate)
                .AnyAsync(ct);

            if (overlaps)
                throw new InvalidOperationException("This absence overlaps one you already have.");

            if (justBecamePending)
            {
                absence.Status = AbsenceStatus.Pending;
                absence.ReviewedBy = null;
                absence.ReviewedAt = null;
                absence.ReviewNote = null;
            }

            absence.StartDate = request.StartDate;
            absence.EndDate = request.EndDate;
            absence.Type = request.Type;
            absence.StartTime = request.StartTime;
            absence.EndTime = request.EndTime;
            absence.Note = request.Note;
            absence.UpdatedAt = DateTime.UtcNow;
            absence.UpdatedBy = person.Username;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // Dates and type, never the note: same reasoning as DeleteAsync.
        await audit.RecordAsync(AuditActions.AbsenceUpdated, person.DisplayName, Describe(absence), ct);

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
        await audit.RecordAsync(AuditActions.AbsenceDeleted, person.DisplayName, Describe(absence), ct);
    }

    // A generous cap on the review queue, not real pagination: this is meant to be worked
    // down to zero, not browsed like the audit log, so a healthy deployment never gets
    // near it. It exists only so a backlog can't make the admin page fetch and render an
    // unbounded response.
    private const int MaxPendingItems = 200;

    // Deliberately not gated on AbsenceApprovals:Enabled, unlike CreateAsync/UpdateAsync:
    // disabling the feature stops new absences from becoming Pending, but a Pending row
    // left over from before that still needs a way to be resolved. Access is gated by the
    // AbsencesApprove permission instead.
    public async Task<PendingAbsencesResponse> GetPendingAsync(CancellationToken ct = default)
    {
        var absences = await db.Absences
            .Where(a => a.Status == AbsenceStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .Take(MaxPendingItems)
            .ToListAsync(ct);

        // Deciding whether to approve is the one situation this app asks someone other
        // than the author to read their note for, so unlike every other read path this
        // includes it unconditionally.
        return new([.. absences.Select(a => AbsenceDto.FromEntity(a, includeNote: true))]);
    }

    public Task<AbsenceDto> ApproveAsync(Person reviewer, int absenceId, CancellationToken ct = default) =>
        ReviewAsync(
            reviewer,
            absenceId,
            note: null,
            AbsenceStatus.Approved,
            unauthorizedMessage: "Cannot approve your own absence.",
            pendingOnlyMessage: "Only a pending request can be approved.",
            AuditActions.AbsenceApproved,
            NotificationType.AbsenceApproved,
            ct);

    public Task<AbsenceDto> RejectAsync(Person reviewer, int absenceId, string note, CancellationToken ct = default) =>
        ReviewAsync(
            reviewer,
            absenceId,
            note,
            AbsenceStatus.Rejected,
            unauthorizedMessage: "Cannot reject your own absence.",
            pendingOnlyMessage: "Only a pending request can be rejected.",
            AuditActions.AbsenceRejected,
            NotificationType.AbsenceRejected,
            ct);

    // Shared by ApproveAsync/RejectAsync, which differ only in the target status and the
    // text/audit/notification that goes with it.
    private async Task<AbsenceDto> ReviewAsync(
        Person reviewer,
        int absenceId,
        string note,
        AbsenceStatus newStatus,
        string unauthorizedMessage,
        string pendingOnlyMessage,
        string auditAction,
        NotificationType notificationType,
        CancellationToken ct)
    {
        var absence = await db.Absences.Include(a => a.Person)
            .FirstOrDefaultAsync(a => a.Id == absenceId, ct)
            ?? throw new KeyNotFoundException($"Absence {absenceId} not found.");

        if (absence.PersonId == reviewer.Id)
            throw new UnauthorizedAccessException(unauthorizedMessage);

        var reviewedAt = DateTime.UtcNow;

        // One transaction for the whole review: audit and notifications share this same
        // context, so if either write fails, the status flip rolls back with them instead
        // of leaving the absence reviewed with no audit entry or notification, and no way
        // back in since a retry would then fail the Pending check below.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // A conditional UPDATE rather than load-check-save: two reviewers racing to review
        // the same absence would otherwise both pass an in-memory Pending check, and the
        // second SaveChangesAsync would silently clobber whatever the first one wrote. This
        // only flips a row that is still Pending, so whichever request loses the race gets
        // pendingOnlyMessage instead of stomping the winner's decision.
        var reviewed = await db.Absences
            .Where(a => a.Id == absenceId && a.Status == AbsenceStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, newStatus)
                .SetProperty(a => a.ReviewedBy, reviewer.Username)
                .SetProperty(a => a.ReviewedAt, reviewedAt)
                .SetProperty(a => a.ReviewNote, note), ct);

        if (reviewed == 0)
            throw new InvalidOperationException(pendingOnlyMessage);

        // ExecuteUpdateAsync bypasses change tracking, so the entity loaded above needs to
        // be brought up to date by hand for Describe/notifications/the response below to
        // see the reviewed state rather than the stale Pending one.
        absence.Status = newStatus;
        absence.ReviewedBy = reviewer.Username;
        absence.ReviewedAt = reviewedAt;
        absence.ReviewNote = note;

        // Dates and type, never the reviewer's reason: that reaches the requester through
        // their notification instead, not through a log other people can read.
        await audit.RecordAsync(auditAction, absence.Person.DisplayName, Describe(absence), ct);
        await notifications.NotifyAsync(absence, notificationType, note, ct);

        await transaction.CommitAsync(ct);

        return AbsenceDto.FromEntity(absence, includeNote: absence.PersonId == reviewer.Id);
    }

    // Approved and Pending both belong on the calendar and both block a new request over
    // the same dates; only Rejected is excluded, since a request that's been turned down
    // shouldn't reserve dates or block a fresh attempt at them.
    private static IQueryable<Absence> VisibleOverlapping(IQueryable<Absence> query, DateOnly from, DateOnly to) =>
        query.Where(a => a.Status != AbsenceStatus.Rejected && a.StartDate <= to && a.EndDate >= from);

    // A time-based request's own overlap check ignores other time-based absences,
    // regardless of type: any number may coexist on the same day, their times unchecked
    // against each other, while a whole-day absence still blocks it. A whole-day request
    // needs no equivalent carve-out here: its own VisibleOverlapping call already sees a
    // same-day time-based absence (StartDate == EndDate) and correctly blocks on it
    // without any change.
    private static IQueryable<Absence> OverlapCandidates(
        IQueryable<Absence> query, bool requestIsTimeBased, DateOnly from, DateOnly to)
    {
        var overlapping = VisibleOverlapping(query, from, to);
        return requestIsTimeBased
            ? overlapping.Where(a => a.StartTime == null)
            : overlapping;
    }

    private static IQueryable<Absence> ApprovedOverlapping(IQueryable<Absence> query, DateOnly from, DateOnly to) =>
        query.Where(a => a.Status == AbsenceStatus.Approved && a.StartDate <= to && a.EndDate >= from);

    private static (DateOnly Start, DateOnly End) YearRange(int year) =>
        (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    private static AbsenceStatsResponse ComputeStats(
        IEnumerable<Absence> absences,
        int year,
        DateOnly yearStart,
        DateOnly yearEnd,
        IReadOnlySet<DateOnly> holidayDates)
    {
        var vacationDays = 0;
        var otherDays = 0;

        foreach (var absence in absences)
        {
            // A time-based absence is hours, not a day off, so it never adds to a day
            // count here, whatever its type.
            if (absence.StartTime is not null) continue;

            var days = CountWorkingDays(
                Max(absence.StartDate, yearStart), Min(absence.EndDate, yearEnd), holidayDates);
            switch (absence.Type)
            {
                case AbsenceType.Vacation: vacationDays += days; break;
                case AbsenceType.Other: otherDays += days; break;
            }
        }

        return new AbsenceStatsResponse(year, vacationDays, otherDays);
    }

    // A day that's both a weekday and this person's own bank holiday (see DatesApplyingTo /
    // HolidayDatesForAsync) isn't a working day either: the calendar paints it as the
    // holiday, not the absence (see the frontend's absenceSegments.ts), and the two have to
    // agree on which days that is.
    private static int CountWorkingDays(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> holidayDates)
    {
        var count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidayDates.Contains(d))
                count++;
        return count;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
