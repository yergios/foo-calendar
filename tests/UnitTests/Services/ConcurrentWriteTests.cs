using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

/// <summary>
/// Two requests racing to write what only one of them may: overlapping absences (ABS-6) and
/// colliding holidays (HOL-7). As in production, the database is a file and each request
/// has a DbContext and a connection of its own, which an in-memory database shared over
/// one connection could not show. The rule check and the write are only atomic because
/// each runs in a serializable transaction; without it, both checks can run before either
/// write, and both requests succeed.
/// </summary>
public sealed class ConcurrentWriteTests : IDisposable
{
    // Each round is one race. A race lost to the scheduler proves nothing either way, so
    // there are enough rounds for the unguarded version to be caught.
    private const int Rounds = 50;

    private readonly string directory = Directory.CreateTempSubdirectory("foo-calendar-race-").FullName;

    public ConcurrentWriteTests()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        // Pooled connections keep the file open, which would stop the directory going.
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={Path.Combine(directory, "race.db")}")
        .Options);

    /// <summary>Runs both writes at once, each on a thread and a context of its own with its
    /// connection already open, and says which succeeded. A rule violation is the one
    /// failure expected; anything else fails the test.</summary>
    private async Task<bool[]> RaceAsync(Func<AppDbContext, Task> first, Func<AppDbContext, Task> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new CountdownEvent(2);

        async Task<bool> Run(Func<AppDbContext, Task> write)
        {
            await using var db = NewContext();
            await db.Database.OpenConnectionAsync();
            // SQLite's own wait for the lock, in milliseconds, rather than the driver's retry,
            // which sleeps 150 ms at a time and would make each round that long.
            await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 10000;");
            ready.Signal();
            await start.Task;
            try
            {
                await write(db);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
            // BankHolidayService's collision still throws this until it moves over.
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var racing = Task.WhenAll(Task.Run(() => Run(first)), Task.Run(() => Run(second)));
        ready.Wait();
        ready.Dispose();
        start.SetResult();
        return await racing;
    }

    private Person AddPerson(int round)
    {
        using var db = NewContext();
        var person = new Person
        {
            Username = $"racer-{round}",
            DisplayName = $"Racer {round}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };
        db.People.Add(person);
        db.SaveChanges();
        return person;
    }

    private static AbsenceService Absences(AppDbContext db) =>
        new(db, new RecordingBankHolidayService(), new RecordingAuditLog());

    private static BankHolidayService Holidays(AppDbContext db) =>
        new(db, new DisabledHolidaySource(), new RecordingAuditLog(), NullLogger<BankHolidayService>.Instance);

    [Fact]
    [Trait("Spec", "ABS-6")]
    [Trait("Spec", "ABS-5")]
    public async Task TwoOverlappingCreatesForOnePerson_OnlyOneSucceeds()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var person = AddPerson(round);
            var monday = new DateOnly(2026, 3, 2);

            var outcomes = await RaceAsync(
                db => Absences(db).CreateAsync(
                    person, new CreateAbsenceRequest(monday, monday.AddDays(4), AbsenceType.Vacation, null)),
                db => Absences(db).CreateAsync(
                    person, new CreateAbsenceRequest(monday.AddDays(4), monday.AddDays(8), AbsenceType.Other, null)));

            outcomes.Count(succeeded => succeeded).ShouldBe(1, $"round {round}");
            using var db = NewContext();
            db.Absences.Count(a => a.PersonId == person.Id).ShouldBe(1, $"round {round}");
        }
    }

    // A recurring holiday occupies its month and day in every year, so it collides with a
    // one-time holiday on that day in a year of its own.
    [Theory]
    [Trait("Spec", "HOL-7")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoCollidingHolidaysInOneRegion_OnlyOneIsAdded(bool recurring)
    {
        string[] tracked = ["CH-ZH"];

        for (var round = 0; round < Rounds; round++)
        {
            var date = new DateOnly(2026, 1, 1).AddDays(round);
            var colliding = recurring ? date.AddYears(1) : date;

            var outcomes = await RaceAsync(
                db => Holidays(db).AddManualAsync(
                    new AddManualHolidayRequest("CH-ZH", date, "First"), "jdoe", tracked),
                db => Holidays(db).AddManualAsync(
                    new AddManualHolidayRequest("CH-ZH", colliding, "Second", recurring), "jdoe", tracked));

            outcomes.Count(succeeded => succeeded).ShouldBe(1, $"round {round}");
            using var db = NewContext();
            db.BankHolidays.AsEnumerable()
                .Count(h => h.Date.Month == date.Month && h.Date.Day == date.Day)
                .ShouldBe(1, $"round {round}");
        }
    }
}
