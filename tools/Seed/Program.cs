using Api.Configuration;
using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Seed;

// Repeatable local dev seed: wipes the SQLite database, re-applies migrations, then
// creates 50 people spread across 10 regions with a mixed set of absences for 2026.
// Run with `dotnet run --project tools/Seed` (or `scripts/seed.sh`), never against a
// deployment that matters, see EnsureDeletedAsync below.
internal static class Program
{
    private const int Year = 2026;
    private const int PeopleCount = 50;

    private static readonly string[] Regions =
    [
        "CH-ZH", // Zurich
        "US-NY", // New York
        "US-CA", // California
        "GB-ENG", // England
        "DE-BE", // Berlin
        "FR-IDF", // Ile-de-France
        "ES-MD", // Madrid
        "JP-13", // Tokyo
        "AU-NSW", // New South Wales
        "CA-ON", // Ontario
    ];

    private static readonly string[] FirstNames =
    [
        "Olivia", "Liam", "Emma", "Noah", "Ava", "Elijah", "Sophia", "Lucas", "Isabella", "Mason",
        "Mia", "Ethan", "Amelia", "Logan", "Harper", "Aiden", "Evelyn", "James", "Abigail", "Benjamin",
        "Ella", "Alexander", "Scarlett", "Michael", "Grace", "Daniel", "Chloe", "Matthew", "Victoria", "Henry",
        "Zoe", "Sebastian", "Lily", "Jack", "Hannah", "Owen", "Layla", "Samuel", "Nora", "David",
        "Riley", "Joseph", "Aria", "Carter", "Zara", "Wyatt", "Maya", "John", "Ruby", "Leo",
    ];

    private static readonly string[] LastNames =
    [
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez",
        "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin",
        "Lee", "Perez", "Thompson", "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson",
        "Walker", "Young", "Allen", "King", "Wright", "Scott", "Torres", "Nguyen", "Hill", "Flores",
        "Green", "Adams", "Nelson", "Baker", "Hall", "Rivera", "Campbell", "Mitchell", "Carter", "Roberts",
    ];

    private static readonly string[] VacationNotes =
        ["Summer trip", "Family vacation", "Long weekend getaway", "Annual leave", null, null];

    private static readonly string[] SickNotes =
        ["Flu", "Cold", "Doctor's orders", "Not feeling well", null, null];

    private static readonly string[] OtherNotes =
        ["Personal errand", "Moving day", "Family matter", "Home repair", null];

    private static readonly string[] AppointmentNotes =
        ["Dentist", "Doctor's appointment", "Car service", "Parent-teacher meeting", "Bank appointment"];

    private static readonly string[] RejectNotes =
    [
        "Coverage needed that week, please pick different dates",
        "Please resubmit with more notice",
        "Team is short-staffed those dates",
    ];

    // Four types, each with its own typical shape: vacations and sick leave are multi-day
    // blocks, appointments are single timed slots, "other" is a mix. Totals to 52 rows per
    // person, comfortably over the 50 minimum.
    private static readonly (AbsenceType Type, int Count, int MinDays, int MaxDays)[] AbsencePlan =
    [
        (AbsenceType.Vacation, 8, 2, 7),
        (AbsenceType.Sick, 10, 1, 3),
        (AbsenceType.Other, 8, 1, 2),
        (AbsenceType.Appointment, 26, 1, 1),
    ];

    private static async Task Main()
    {
        // Fixed on purpose: re-running the script must produce the exact same dataset,
        // not a new random one each time.
        var rng = new Random(20260101);

        var repoRoot = FindRepoRoot();
        var apiDir = Path.Combine(repoRoot, "src", "Api");

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDir)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(ConnectionStrings.FooCalendarDb)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStrings.FooCalendarDb} must be configured.");

        // appsettings.json's "Data Source=foo-calendar.db" is relative to the API
        // project's own directory at runtime, not to wherever this tool is invoked from.
        var connectionBuilder = new SqliteConnectionStringBuilder(connectionString);
        if (!Path.IsPathRooted(connectionBuilder.DataSource))
            connectionBuilder.DataSource = Path.Combine(apiDir, connectionBuilder.DataSource);

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionBuilder.ConnectionString);

        await using var db = new AppDbContext(optionsBuilder.Options);

        Console.WriteLine($"Resetting database at {connectionBuilder.DataSource} ...");
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        Console.WriteLine($"Seeding {PeopleCount} people across {Regions.Length} regions ...");
        var people = BuildPeople(rng);
        db.People.AddRange(people);
        await db.SaveChangesAsync();

        Console.WriteLine("Seeding absences for 2026 ...");
        var absenceCount = 0;
        foreach (var person in people)
        {
            var absences = BuildAbsencesFor(person, rng);
            db.Absences.AddRange(absences);
            absenceCount += absences.Count;
        }
        await db.SaveChangesAsync();

        Console.WriteLine($"Done: {people.Count} people, {absenceCount} absences "
            + $"(avg {absenceCount / (double)people.Count:F1} per person).");

        // Inserted straight into the database above, unlike a real signup or CSV import,
        // which both go through PersonService and so trigger TeamRegionService's sync as a
        // side effect of saving. Nothing does that here, so without this the seeded people
        // would show up with none of their regions' bank holidays until the next scheduled
        // sync (up to a week later, see HolidayOptions.SyncInterval).
        await SyncHolidaysAsync(db, configuration);
    }

    private static async Task SyncHolidaysAsync(AppDbContext db, IConfiguration configuration)
    {
        var regions = Regions.Distinct(StringComparer.Ordinal).ToList();
        Console.WriteLine($"Syncing bank holidays for {regions.Count} region(s) ...");

        var holidayOptions = configuration.GetSection(HolidayOptions.SectionName).Get<HolidayOptions>()
            ?? new HolidayOptions();
        var source = BuildHolidaySource(holidayOptions);
        var bankHolidays = new BankHolidayService(
            db, source, new NoOpAuditLog(), NullLogger<BankHolidayService>.Instance);

        // Same two years the background job keeps warm and a profile save syncs on the
        // spot, see TeamRegionService.EnsureSyncedAsync.
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var year in new[] { Year, Year + 1 })
            failed.UnionWith(await bankHolidays.SyncRegionsAsync(regions, year, default));

        Console.WriteLine(failed.Count == 0
            ? "Done: bank holidays synced."
            : $"Done: bank holidays synced, but {failed.Count} region(s) failed: {string.Join(", ", failed)}.");
    }

    private static IHolidaySource BuildHolidaySource(HolidayOptions options) => options.Source switch
    {
        HolidayOptions.SourceFile => new FileHolidaySource(Options.Create(options)),
        HolidayOptions.SourceNone => new DisabledHolidaySource(),
        // Nager, the default: needs no client-side config beyond its base URL, same as
        // Program.cs's own registration.
        _ => new NagerHolidaySource(new HttpClient { BaseAddress = new Uri(options.Nager.BaseUrl) }),
    };

    // Stands in for the real AuditLog, which needs a signed-in user (IHttpContextAccessor)
    // that a console script doesn't have. The seed script doesn't audit-log the people or
    // absences it creates either, so staying silent here is consistent, not a shortcut.
    private sealed class NoOpAuditLog : IAuditLog
    {
        public Task RecordAsync(
            string action, string target, string details = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<AuditEntriesResponse> ReadAsync(
            int limit,
            int offset,
            string search = null,
            string action = null,
            DateTime? from = null,
            DateTime? to = null,
            string sortBy = AuditSortColumn.OccurredAt,
            bool descending = true,
            CancellationToken ct = default) =>
            throw new NotSupportedException("The seed script never reads the audit log.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FooCalendar.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (FooCalendar.slnx not found).");
    }

    private static List<Person> BuildPeople(Random rng)
    {
        var shuffledLastNames = LastNames.OrderBy(_ => rng.Next()).ToArray();
        var people = new List<Person>(PeopleCount);

        for (var i = 0; i < PeopleCount; i++)
        {
            var firstName = FirstNames[i];
            var lastName = shuffledLastNames[i];
            var username = $"{firstName[..1]}{lastName}".ToLowerInvariant();
            var region = Regions[i % Regions.Length]; // 5 people per region, evenly spread

            people.Add(new Person
            {
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = $"{firstName} {lastName}",
                Email = $"{username}@example.test",
                HomeCity = region,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed-script",
            });
        }

        return people;
    }

    private static List<Absence> BuildAbsencesFor(Person person, Random rng)
    {
        var yearDays = Enumerable.Range(0, DateTime.IsLeapYear(Year) ? 366 : 365)
            .Select(offset => new DateOnly(Year, 1, 1).AddDays(offset))
            .ToList();
        var usedDates = new HashSet<DateOnly>();

        var absences = new List<Absence>();

        foreach (var (type, count, minDays, maxDays) in AbsencePlan)
        {
            for (var i = 0; i < count; i++)
            {
                var length = rng.Next(minDays, maxDays + 1);
                var start = TryPlaceBlock(yearDays, usedDates, length, rng);
                if (start is null)
                    continue; // year's full for this person; skip rather than overlap

                absences.Add(BuildAbsence(person, type, start.Value, length, rng));
            }
        }

        return absences;
    }

    private static DateOnly? TryPlaceBlock(
        List<DateOnly> yearDays, HashSet<DateOnly> usedDates, int length, Random rng)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var startIndex = rng.Next(0, yearDays.Count - length + 1);
            var candidate = yearDays.GetRange(startIndex, length);
            if (candidate.Any(usedDates.Contains))
                continue;

            foreach (var date in candidate)
                usedDates.Add(date);

            return candidate[0];
        }

        return null;
    }

    private static Absence BuildAbsence(Person person, AbsenceType type, DateOnly start, int length, Random rng)
    {
        var absence = new Absence
        {
            PersonId = person.Id,
            StartDate = start,
            EndDate = start.AddDays(length - 1),
            Type = type,
            Note = PickNote(type, rng),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = person.Username,
        };

        // Always timed for Appointment, sometimes timed for Other, never for Vacation/Sick.
        if (type == AbsenceType.Appointment || (type == AbsenceType.Other && rng.Next(2) == 0))
        {
            var startTime = new TimeOnly(rng.Next(8, 17), 15 * rng.Next(4));
            absence.StartTime = startTime;
            absence.EndTime = startTime.AddMinutes(new[] { 30, 45, 60, 90 }[rng.Next(4)]);
        }

        ApplyReviewState(absence, rng);
        return absence;
    }

    private static void ApplyReviewState(Absence absence, Random rng)
    {
        var roll = rng.Next(100);
        absence.Status = roll switch
        {
            < 90 => AbsenceStatus.Approved,
            < 95 => AbsenceStatus.Pending,
            _ => AbsenceStatus.Rejected,
        };

        if (absence.Status == AbsenceStatus.Pending)
            return;

        absence.ReviewedBy = "dev-user";
        absence.ReviewedAt = absence.CreatedAt;
        if (absence.Status == AbsenceStatus.Rejected)
            absence.ReviewNote = RejectNotes[rng.Next(RejectNotes.Length)];
    }

    private static string PickNote(AbsenceType type, Random rng)
    {
        var pool = type switch
        {
            AbsenceType.Vacation => VacationNotes,
            AbsenceType.Sick => SickNotes,
            AbsenceType.Appointment => AppointmentNotes,
            _ => OtherNotes,
        };

        return pool[rng.Next(pool.Length)];
    }
}
