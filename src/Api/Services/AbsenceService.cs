using System.Data;

using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class AbsenceService(
    AppDbContext db,
    IBankHolidayService bankHolidays,
    IAuditLog audit) : IAbsenceService
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
        var absences = await Overlapping(db.Absences, from, to)
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

        var absences = await Overlapping(db.Absences.Where(a => a.PersonId == person.Id), yearStart, yearEnd)
            .ToListAsync(ct);

        var holidayDates = await HolidayDatesForAsync(person.HomeCity, yearStart, yearEnd, ct);

        return ComputeStats(absences, year, yearStart, yearEnd, holidayDates);
    }

    public async Task<AllAbsenceStatsResponse> GetAllStatsAsync(int year, CancellationToken ct = default)
    {
        var (yearStart, yearEnd) = YearRange(year);

        var people = await db.People.Select(p => new { p.Id, p.HomeCity }).ToListAsync(ct);
        var absences = await Overlapping(db.Absences, yearStart, yearEnd).ToListAsync(ct);
        var byPerson = absences.ToLookup(a => a.PersonId);

        // One fetch covering every region anyone's home city touches, rather than one round
        // trip per person: a team's home regions are few, however many people share them.
        var regions = people.Select(p => p.HomeCity).Where(c => c is not null).Distinct().ToList();
        var holidays = regions.Count > 0
            ? (await bankHolidays.GetForRegionsAsync(regions, yearStart, yearEnd, ct)).Holidays
            : [];
        var holidaysByRegion = holidays.ToLookup(h => h.Region);

        // Built once per distinct region rather than once per person: everyone sharing a
        // home city ends up with the exact same set of holiday dates, so there's nothing to
        // gain from rebuilding it for each of them.
        var holidayDatesByCity = regions.ToDictionary(c => c, c => DatesApplyingTo(c, holidaysByRegion));

        var stats = people
            .Select(p =>
            {
                var holidayDates = p.HomeCity is null ? [] : holidayDatesByCity[p.HomeCity];
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
    // belonging to one person's country sits alongside another's; this picks back out just
    // the ones that are this region's own, via the same self-plus-country expansion
    // GetForRegionsAsync itself fetched by. Grouped by region once up front rather than
    // filtered per person: a per-person scan over every holiday in play would cost
    // O(people x holidays) for what a lookup answers in two key hits each.
    private static HashSet<DateOnly> DatesApplyingTo(
        string region, ILookup<string, BankHolidayDto> holidaysByRegion) =>
        RegionCode.SelfAndCountry(region)
            .SelectMany(r => holidaysByRegion[r])
            .Select(h => h.Date)
            .ToHashSet();

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
                throw new ConflictException("This absence overlaps one you already have.");

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
            ?? throw new NotFoundException($"Absence {absenceId} not found.");

        if (absence.PersonId != person.Id)
            throw new NotOwnerException("Cannot edit another person's absence.");

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
                throw new ConflictException("This absence overlaps one you already have.");

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
            ?? throw new NotFoundException($"Absence {absenceId} not found.");

        if (absence.PersonId != person.Id)
            throw new NotOwnerException("Cannot delete another person's absence.");

        db.Absences.Remove(absence);
        await db.SaveChangesAsync(ct);

        // Dates and type, never the note: the log is read by people the note was never
        // meant for, and a deleted absence shouldn't outlive its author's privacy.
        await audit.RecordAsync(AuditActions.AbsenceDeleted, person.DisplayName, Describe(absence), ct);
    }

    private static IQueryable<Absence> Overlapping(IQueryable<Absence> query, DateOnly from, DateOnly to) =>
        query.Where(a => a.StartDate <= to && a.EndDate >= from);

    // A time-based request's own overlap check ignores other time-based absences,
    // regardless of type: any number may coexist on the same day, their times unchecked
    // against each other, while a whole-day absence still blocks it. A whole-day request
    // needs no equivalent carve-out here: its own Overlapping call already sees a same-day
    // time-based absence (StartDate == EndDate) and correctly blocks on it without any
    // change.
    private static IQueryable<Absence> OverlapCandidates(
        IQueryable<Absence> query, bool requestIsTimeBased, DateOnly from, DateOnly to)
    {
        var overlapping = Overlapping(query, from, to);
        return requestIsTimeBased
            ? overlapping.Where(a => a.StartTime == null)
            : overlapping;
    }

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
