using System.Globalization;

using Api.Configuration;
using Api.Data.Models;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class RetentionServiceTests : SqliteTestBase
{
    private RetentionService CreateSut(RetentionOptions options) =>
        new(Db, Options.Create(options), NullLogger<RetentionService>.Instance);

    private void AddAuditEntry(int daysAgo)
    {
        Db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = DateTime.UtcNow.AddDays(-daysAgo),
            Actor = "jdoe",
            Action = AuditActions.AbsenceCreated,
            Target = "Jane Doe",
        });
        Db.SaveChanges();
    }

    private Person AddPerson(string username = "jdoe")
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    private void AddAbsence(int personId, string start, string end)
    {
        Db.Absences.Add(new Absence
        {
            PersonId = personId,
            StartDate = DateOnly.Parse(start, CultureInfo.InvariantCulture),
            EndDate = DateOnly.Parse(end, CultureInfo.InvariantCulture),
            Type = AbsenceType.Vacation,
            CreatedBy = "jdoe",
        });
        Db.SaveChanges();
    }

    [Fact]
    [Trait("Spec", "PRIV-2")]
    public async Task SweepAsync_DeletesAuditEntriesPastTheirRetention()
    {
        AddAuditEntry(daysAgo: 400);
        AddAuditEntry(daysAgo: 10);

        var result = await CreateSut(new RetentionOptions { AuditEntryDays = 365 }).SweepAsync();

        result.AuditEntries.ShouldBe(1);
        Db.AuditEntries.Count().ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "PRIV-2")]
    public async Task SweepAsync_KeepsAnEntryStillInsideItsRetention()
    {
        // A day inside the window, not exactly on it: the cutoff moves with the clock, so
        // an entry timestamped at exactly the boundary races the sweep's own "now" by
        // however long the test took to get there. Retention is configured in days, and
        // this is the part of the boundary that means anything at that scale.
        AddAuditEntry(daysAgo: 364);

        var result = await CreateSut(new RetentionOptions { AuditEntryDays = 365 }).SweepAsync();

        result.AuditEntries.ShouldBe(0);
        Db.AuditEntries.Count().ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "PRIV-2")]
    public async Task SweepAsync_WithRetentionOff_KeepsEverything()
    {
        AddAuditEntry(daysAgo: 4000);
        var person = AddPerson();
        AddAbsence(person.Id, "2015-01-05", "2015-01-09");

        var options = new RetentionOptions
        {
            AuditEntryDays = RetentionOptions.Never,
            AbsenceDays = RetentionOptions.Never,
        };
        var result = await CreateSut(options).SweepAsync();

        result.Total.ShouldBe(0);
        Db.AuditEntries.Count().ShouldBe(1);
        Db.Absences.Count().ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "PRIV-2")]
    public async Task SweepAsync_LeavesAbsencesAloneByDefault()
    {
        var person = AddPerson();
        AddAbsence(person.Id, "2015-01-05", "2015-01-09");

        var result = await CreateSut(new RetentionOptions()).SweepAsync();

        result.Absences.ShouldBe(0);
        Db.Absences.Count().ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "PRIV-2")]
    public async Task SweepAsync_MeasuresAnAbsenceFromWhenItEnded()
    {
        // Booked long ago but still running: it is current, whatever its start date says.
        var person = AddPerson();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        AddAbsence(person.Id, today.AddDays(-400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                              today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddAbsence(person.Id, today.AddDays(-500).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                              today.AddDays(-450).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var result = await CreateSut(new RetentionOptions { AbsenceDays = 365 }).SweepAsync();

        result.Absences.ShouldBe(1);
        Db.Absences.Single().EndDate.ShouldBe(today.AddDays(30));
    }

    [Fact]
    [Trait("Spec", "PRIV-2")]
    public async Task SweepAsync_ReportsWhatItDeletedPerTable()
    {
        var person = AddPerson();
        AddAuditEntry(daysAgo: 400);
        AddAbsence(person.Id, "2015-01-05", "2015-01-09");

        var options = new RetentionOptions
        {
            AuditEntryDays = 365,
            AbsenceDays = 365,
        };
        var result = await CreateSut(options).SweepAsync();

        result.AuditEntries.ShouldBe(1);
        result.Absences.ShouldBe(1);
        result.Total.ShouldBe(2);
    }

    // Deleting a person is the app's answer to an erasure request, so what goes with them
    // is a decision AppDbContext states explicitly rather than inherits.
    [Fact]
    [Trait("Spec", "PPL-6")]
    [Trait("Spec", "AUD-3")]
    public async Task DeletingAPerson_TakesTheirAbsencesButNotTheAuditLog()
    {
        var person = AddPerson();
        AddAbsence(person.Id, "2026-01-05", "2026-01-09");
        AddAuditEntry(daysAgo: 1);

        Db.People.Remove(person);
        await Db.SaveChangesAsync();

        Db.Absences.Count().ShouldBe(0);
        Db.AuditEntries.Count().ShouldBe(1);
    }
}
