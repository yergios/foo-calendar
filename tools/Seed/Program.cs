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
// creates people spread across regions with a mixed set of absences for 2026. Sizes
// default to 50 people over 10 regions and are overridable, see PrintUsage.
// Run with `dotnet run --project tools/Seed` (or `scripts/seed.sh`), never against a
// deployment that matters, see EnsureDeletedAsync below.
internal static class Program
{
    private const int Year = 2026;
    private const int DefaultPeopleCount = 50;
    private const int DefaultRegionCount = 10;

    // --regions takes a prefix of this list, so raising the count only adds regions and
    // never renames the ones a smaller run already produced. The first ten are the
    // long-standing default set, which is why they aren't sorted with the rest.
    // Every code is ISO 3166-2 as found in src/Api/Data/regions.json, and every country
    // behind one is covered by Nager, so the holiday sync has something to return.
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
        "AR-C", // Buenos Aires
        "AT-9", // Vienna
        "AT-1", // Burgenland
        "BE-BRU", // Brussels
        "BG-22", // Sofia
        "BR-SP", // Sao Paulo
        "CA-BC", // British Columbia
        "CA-QC", // Quebec
        "CH-GE", // Geneva
        "CL-RM", // Santiago Metropolitan
        "CO-DC", // Bogota
        "CZ-10", // Prague
        "DE-BY", // Bavaria
        "DE-NW", // North Rhine-Westphalia
        "DK-84", // Capital Region
        "EC-P", // Pichincha
        "EE-37", // Harju
        "ES-CT", // Catalonia
        "FI-18", // Uusimaa
        "FR-PAC", // Provence-Alpes-Cote d'Azur
        "GB-SCT", // Scotland
        "GB-WLS", // Wales
        "GR-A", // East Macedonia and Thrace
        "HR-21", // Zagreb
        "HU-BU", // Budapest
        "IE-D", // Dublin
        "IS", // Iceland
        "IT-25", // Lombardy
        "KR-11", // Seoul
        "LT-VL", // Vilnius County
        "LU", // Luxembourg
        "LV-RIX", // Riga
        "MX-CMX", // Mexico City
        "NL-NH", // North Holland
        "NO-03", // Oslo
        "NZ-AUK", // Auckland
        "PE-LMA", // Lima
        "PL-14", // Mazovia
        "PT-11", // Lisbon
        "RO-B", // Bucharest
        "SE-AB", // Stockholm
        "SG", // Singapore
        "SI-061", // Ljubljana
        "SK-BL", // Bratislava
        "TR-34", // Istanbul
        "UA-30", // Kyiv
        "US-TX", // Texas
        "US-WA", // Washington
        "UY-MO", // Montevideo
        "ZA-GP", // Gauteng
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

    // Nothing here says why someone was unwell: seed data ends up in screenshots and demo
    // deployments, and the app deliberately has nowhere to record that (see AbsenceType).
    private static readonly string[] OtherNotes =
        ["Personal errand", "Moving day", "Family matter", "Home repair", null, null];

    private static readonly string[] AppointmentNotes =
        ["Dentist", "Doctor's appointment", "Car service", "Parent-teacher meeting", "Bank appointment"];

    // Three types, each with its own typical shape: vacations are multi-day blocks,
    // appointments are single timed slots, "other" is a mix - it covers the short unplanned
    // absences too, since there is no Sick type (see Api's AbsenceType). Totals to 52 rows
    // per person, comfortably over the 50 minimum.
    private static readonly (AbsenceType Type, int Count, int MinDays, int MaxDays)[] AbsencePlan =
    [
        (AbsenceType.Vacation, 8, 2, 7),
        (AbsenceType.Other, 18, 1, 3),
        (AbsenceType.Appointment, 26, 1, 1),
    ];

    // Every absence is placed inside the same year for everybody, so the candidate days are
    // built once here instead of per person.
    private static readonly DateOnly[] YearDays =
    [
        .. Enumerable.Range(0, DateTime.IsLeapYear(Year) ? 366 : 365)
            .Select(offset => new DateOnly(Year, 1, 1).AddDays(offset))
    ];

    // Distinct first/last pairs available to BuildPeople, and so the ceiling on --people.
    private static readonly int MaxPeopleCount = FirstNames.Length * LastNames.Length;

    private static async Task<int> Main(string[] args)
    {
        (int People, int Regions)? parsed;
        try
        {
            parsed = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage(Console.Error);
            return 2;
        }

        if (parsed is null) // --help
        {
            PrintUsage(Console.Out);
            return 0;
        }

        var (peopleCount, regionCount) = parsed.Value;

        // Fixed on purpose: re-running the script must produce the exact same dataset,
        // not a new random one each time.
        var rng = new Random(20260101);

        var regions = Regions[..regionCount];

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

        // A large --people run inserts six figures of rows in two batches. Change detection
        // rescans everything already tracked on each AddRange, which turns that into
        // quadratic work for no benefit here: nothing below mutates an entity after adding
        // it. Both settings are put back before the holiday sync, which does rely on them.
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        Console.WriteLine($"Seeding {peopleCount} people across {regionCount} regions ...");
        var people = BuildPeople(peopleCount, regions, rng);
        db.People.AddRange(people);
        await db.SaveChangesAsync();

        Console.WriteLine($"Seeding absences for {Year} ...");
        var absenceCount = 0;
        foreach (var person in people)
        {
            var absences = BuildAbsencesFor(person, rng);
            db.Absences.AddRange(absences);
            absenceCount += absences.Count;
        }
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        Console.WriteLine($"Done: {people.Count} people, {absenceCount} absences "
            + $"(avg {absenceCount / (double)people.Count:F1} per person).");

        // Inserted straight into the database above, unlike a real signup or CSV import,
        // which both go through PersonService and so trigger TeamRegionService's sync as a
        // side effect of saving. Nothing does that here, so without this the seeded people
        // would show up with none of their regions' bank holidays until the next scheduled
        // sync (up to a week later, see HolidayOptions.SyncInterval).
        await SyncHolidaysAsync(db, configuration, regions);
        return 0;
    }

    // Named flags rather than positionals: "seed.sh 200 20" reads the same either way round,
    // and getting it backwards is silent.
    private static (int People, int Regions)? ParseArgs(string[] args)
    {
        var people = DefaultPeopleCount;
        var regions = DefaultRegionCount;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--people" or "-p":
                    people = ReadCount(args, ref i);
                    break;
                case "--regions" or "-r":
                    regions = ReadCount(args, ref i);
                    break;
                case "--help" or "-h":
                    return null;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (people < 1 || people > MaxPeopleCount)
            throw new ArgumentException(
                $"--people must be between 1 and {MaxPeopleCount} (the number of distinct "
                + "first/last name pairs available); add names to Program.cs to raise it.");

        if (regions < 1 || regions > Regions.Length)
            throw new ArgumentException(
                $"--regions must be between 1 and {Regions.Length} (the region codes known to "
                + "Program.cs).");

        return (people, regions);
    }

    private static int ReadCount(string[] args, ref int i)
    {
        var name = args[i];
        if (++i == args.Length)
            throw new ArgumentException($"{name} needs a number, e.g. \"{name} 50\".");

        return int.TryParse(args[i], out var count)
            ? count
            : throw new ArgumentException($"{name} needs a whole number, got '{args[i]}'.");
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: dotnet run --project tools/Seed -- [options]");
        output.WriteLine("   or: scripts/seed.sh [options]");
        output.WriteLine();
        output.WriteLine($"  -p, --people N   people to create (default {DefaultPeopleCount}, "
            + $"max {MaxPeopleCount})");
        output.WriteLine($"  -r, --regions N  regions to spread them across (default "
            + $"{DefaultRegionCount}, max {Regions.Length})");
        output.WriteLine("  -h, --help       show this message");
        output.WriteLine();
        output.WriteLine("Every run empties the database first, so the same options always");
        output.WriteLine("produce the same dataset.");
    }

    private static async Task SyncHolidaysAsync(
        AppDbContext db, IConfiguration configuration, IReadOnlyList<string> selected)
    {
        var regions = selected.Distinct(StringComparer.Ordinal).ToList();
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

    private static List<Person> BuildPeople(int count, IReadOnlyList<string> regions, Random rng)
    {
        var shuffledLastNames = LastNames.OrderBy(_ => rng.Next()).ToArray();
        var people = new List<Person>(count);
        var usernames = new HashSet<string>(count, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            // Walking the last names one step further on every pass through the first names
            // pairs each first name with a different last name in each pass, so all
            // FirstNames.Length * LastNames.Length combinations are distinct.
            var firstName = FirstNames[i % FirstNames.Length];
            var lastName = shuffledLastNames[(i + i / FirstNames.Length) % LastNames.Length];
            var username = MakeUsername(firstName, lastName, usernames);
            var region = regions[i % regions.Count]; // evenly spread

            people.Add(new Person
            {
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = $"{firstName} {lastName}",
                HomeCity = region,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "seed-script",
            });
        }

        return people;
    }

    // First initial plus last name, which is unique across the default 50 but starts to
    // collide once the name pool wraps ("Olivia Smith" and "Owen Smith" both want "osmith"),
    // and Person.Username is uniquely indexed.
    private static string MakeUsername(string firstName, string lastName, HashSet<string> taken)
    {
        var stem = $"{firstName[..1]}{lastName}".ToLowerInvariant();

        var username = stem;
        for (var suffix = 2; !taken.Add(username); suffix++)
            username = $"{stem}{suffix}";

        return username;
    }

    private static List<Absence> BuildAbsencesFor(Person person, Random rng)
    {
        var usedDates = new HashSet<DateOnly>();

        var absences = new List<Absence>();

        foreach (var (type, count, minDays, maxDays) in AbsencePlan)
        {
            for (var i = 0; i < count; i++)
            {
                var length = rng.Next(minDays, maxDays + 1);
                var start = TryPlaceBlock(usedDates, length, rng);
                if (start is null)
                    continue; // year's full for this person; skip rather than overlap

                absences.Add(BuildAbsence(person, type, start.Value, length, rng));
            }
        }

        return absences;
    }

    private static DateOnly? TryPlaceBlock(HashSet<DateOnly> usedDates, int length, Random rng)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var startIndex = rng.Next(0, YearDays.Length - length + 1);
            var candidate = YearDays.AsSpan(startIndex, length);
            if (Overlaps(candidate, usedDates))
                continue;

            foreach (var date in candidate)
                usedDates.Add(date);

            return candidate[0];
        }

        return null;
    }

    private static bool Overlaps(ReadOnlySpan<DateOnly> candidate, HashSet<DateOnly> usedDates)
    {
        foreach (var date in candidate)
            if (usedDates.Contains(date))
                return true;

        return false;
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

        // Always timed for Appointment, sometimes timed for Other, never for Vacation.
        if (type == AbsenceType.Appointment || (type == AbsenceType.Other && rng.Next(2) == 0))
        {
            var startTime = new TimeOnly(rng.Next(8, 17), 15 * rng.Next(4));
            absence.StartTime = startTime;
            absence.EndTime = startTime.AddMinutes(new[] { 30, 45, 60, 90 }[rng.Next(4)]);
        }

        return absence;
    }

    private static string PickNote(AbsenceType type, Random rng)
    {
        var pool = type switch
        {
            AbsenceType.Vacation => VacationNotes,
            AbsenceType.Appointment => AppointmentNotes,
            _ => OtherNotes,
        };

        return pool[rng.Next(pool.Length)];
    }
}
