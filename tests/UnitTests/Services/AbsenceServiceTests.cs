using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class AbsenceServiceTests : SqliteTestBase
{
    /// <summary>Records what the service published instead of delivering anything.</summary>
    private sealed class RecordingWebhookPublisher : IWebhookPublisher
    {
        public List<WebhookEvent> Published { get; } = [];

        public void Publish(WebhookEvent webhookEvent) => Published.Add(webhookEvent);
    }

    private readonly RecordingWebhookPublisher webhooks = new();
    private readonly RecordingAuditLog audit = new();
    private readonly AbsenceService sut;

    public AbsenceServiceTests()
    {
        sut = new AbsenceService(Db, webhooks, audit);
    }

    private Person CreatePerson(string username = "jdoe", string color = "#111111")
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            Color = color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    private static CreateAbsenceRequest Request(
        string start, string end, AbsenceType type = AbsenceType.Vacation, string note = null) =>
        new(DateOnly.Parse(start), DateOnly.Parse(end), type, note);

    [Fact]
    public async Task CreateAsync_WhenNoOverlap_CreatesAbsence()
    {
        var person = CreatePerson();

        var result = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        result.Id.ShouldBeGreaterThan(0);
        result.PersonId.ShouldBe(person.Id);
    }

    [Fact]
    public async Task CreateAsync_WhenDatesOverlapAnExistingAbsence_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-03-05", "2026-03-10")));
    }

    [Fact]
    public async Task CreateAsync_WhenNewAbsenceStartsOnExistingEndDate_Throws()
    {
        // Touching ranges (new start == existing end) still share that one day, so it counts
        // as an overlap, not a valid back-to-back pair.
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-03-06", "2026-03-10")));
    }

    [Fact]
    public async Task CreateAsync_WhenNewAbsenceStartsTheDayAfterExistingEnds_Succeeds()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        var result = await sut.CreateAsync(person, Request("2026-03-07", "2026-03-10"));

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateAsync_DoesNotConflictWithAnotherPersonsOverlappingAbsence()
    {
        var alice = CreatePerson("alice", "#111111");
        var bob = CreatePerson("bob", "#222222");
        await sut.CreateAsync(alice, Request("2026-03-02", "2026-03-06"));

        var result = await sut.CreateAsync(bob, Request("2026-03-02", "2026-03-06"));

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_WhenDatesUnchanged_DoesNotConflictWithItself()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        var updated = await sut.UpdateAsync(
            person, created.Id, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "updated"));

        updated.Type.ShouldBe(AbsenceType.Sick);
        updated.Note.ShouldBe("updated");
    }

    [Fact]
    public async Task UpdateAsync_WhenNotOwner_ThrowsUnauthorizedAccessException()
    {
        var owner = CreatePerson("alice", "#111111");
        var intruder = CreatePerson("bob", "#222222");
        var created = await sut.CreateAsync(owner, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => sut.UpdateAsync(intruder, created.Id, Request("2026-04-01", "2026-04-05")));
    }

    [Fact]
    public async Task UpdateAsync_WhenAbsenceDoesNotExist_ThrowsKeyNotFoundException()
    {
        var person = CreatePerson();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => sut.UpdateAsync(person, 999, Request("2026-04-01", "2026-04-05")));
    }

    [Fact]
    public async Task UpdateAsync_WhenNewDatesOverlapADifferentAbsence_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-01-05", "2026-01-10"));
        var second = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateAsync(person, second.Id, Request("2026-01-08", "2026-01-12")));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotOwner_ThrowsUnauthorizedAccessException()
    {
        var owner = CreatePerson("alice", "#111111");
        var intruder = CreatePerson("bob", "#222222");
        var created = await sut.CreateAsync(owner, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => sut.DeleteAsync(intruder, created.Id));
    }

    [Fact]
    public async Task DeleteAsync_WhenAbsenceDoesNotExist_ThrowsKeyNotFoundException()
    {
        var person = CreatePerson();

        await Should.ThrowAsync<KeyNotFoundException>(() => sut.DeleteAsync(person, 999));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheAbsence()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await sut.DeleteAsync(person, created.Id);

        var remaining = await sut.GetOverlappingAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), person.Id);
        remaining.Absences.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStatsAsync_CountsOnlyWeekdays()
    {
        var person = CreatePerson();
        // Mon 2026-03-02 through Sun 2026-03-08: one full calendar week, 5 working days.
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-08"));

        var stats = await sut.GetStatsAsync(person.Id, 2026);

        stats.VacationDays.ShouldBe(5);
    }

    [Fact]
    public async Task GetStatsAsync_ClipsAbsenceToTheRequestedYear()
    {
        var person = CreatePerson();
        // Wed 2025-12-31 through Fri 2026-01-02: only Jan 1 (Thu) and Jan 2 (Fri) fall in 2026.
        await sut.CreateAsync(person, Request("2025-12-31", "2026-01-02"));

        var stats = await sut.GetStatsAsync(person.Id, 2026);

        stats.VacationDays.ShouldBe(2);
    }

    [Fact]
    public async Task GetStatsAsync_SplitsDaysByAbsenceType()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-02-02", "2026-02-02", AbsenceType.Vacation));
        await sut.CreateAsync(person, Request("2026-02-03", "2026-02-03", AbsenceType.Sick));
        await sut.CreateAsync(person, Request("2026-02-04", "2026-02-04", AbsenceType.Other));

        var stats = await sut.GetStatsAsync(person.Id, 2026);

        stats.VacationDays.ShouldBe(1);
        stats.SickDays.ShouldBe(1);
        stats.OtherDays.ShouldBe(1);
    }

    [Fact]
    public async Task GetStatsAsync_ExcludesNonApprovedAbsences()
    {
        var person = CreatePerson();
        Db.Absences.Add(new Absence
        {
            PersonId = person.Id,
            StartDate = new DateOnly(2026, 5, 4),
            EndDate = new DateOnly(2026, 5, 8),
            Type = AbsenceType.Vacation,
            Status = AbsenceStatus.Pending,
            CreatedBy = person.Username,
        });
        Db.SaveChanges();

        var stats = await sut.GetStatsAsync(person.Id, 2026);

        stats.VacationDays.ShouldBe(0);
    }

    [Fact]
    public async Task GetAllStatsAsync_ReturnsZeroForPersonWithNoAbsences()
    {
        var person = CreatePerson();

        var stats = await sut.GetAllStatsAsync(2026);

        var personStats = stats.Stats.ShouldHaveSingleItem();
        personStats.PersonId.ShouldBe(person.Id);
        personStats.VacationDays.ShouldBe(0);
        personStats.SickDays.ShouldBe(0);
        personStats.OtherDays.ShouldBe(0);
    }

    [Fact]
    public async Task GetAllStatsAsync_ComputesEachPersonIndependently()
    {
        var alice = CreatePerson("alice", "#111111");
        var bob = CreatePerson("bob", "#222222");
        // Mon 2026-06-01 through Fri 2026-06-05: 5 consecutive working days.
        await sut.CreateAsync(alice, Request("2026-06-01", "2026-06-05"));

        var stats = await sut.GetAllStatsAsync(2026);

        stats.Stats.Single(s => s.PersonId == alice.Id).VacationDays.ShouldBe(5);
        stats.Stats.Single(s => s.PersonId == bob.Id).VacationDays.ShouldBe(0);
    }

    [Fact]
    public async Task GetOverlappingAsync_ReturnsOnlyApprovedAbsencesWithinRange()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-07-06", "2026-07-10"));
        Db.Absences.Add(new Absence
        {
            PersonId = person.Id,
            StartDate = new DateOnly(2026, 7, 6),
            EndDate = new DateOnly(2026, 7, 10),
            Type = AbsenceType.Sick,
            Status = AbsenceStatus.Pending,
            CreatedBy = person.Username,
        });
        Db.SaveChanges();

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), person.Id);

        var absence = result.Absences.ShouldHaveSingleItem();
        absence.Type.ShouldBe(AbsenceType.Vacation);
    }

    [Fact]
    public async Task CreateAsync_PublishesAnAbsenceCreatedEvent()
    {
        var person = CreatePerson();

        var created = await sut.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5), AbsenceType.Vacation, "Trip"));

        var published = webhooks.Published.ShouldHaveSingleItem();
        published.Event.ShouldBe(WebhookEvents.AbsenceCreated);
        published.Actor.ShouldBe(person.Username);
        published.Absence.Id.ShouldBe(created.Id);
        published.Absence.PersonName.ShouldBe(person.DisplayName);
        published.Absence.Type.ShouldBe(AbsenceType.Vacation);
        published.Absence.Note.ShouldBe("Trip");
    }

    [Fact]
    public async Task UpdateAsync_PublishesAnAbsenceUpdatedEventWithTheNewDates()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5), AbsenceType.Vacation, null));
        webhooks.Published.Clear();

        await sut.UpdateAsync(
            person,
            created.Id,
            new CreateAbsenceRequest(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), AbsenceType.Sick, null));

        var published = webhooks.Published.ShouldHaveSingleItem();
        published.Event.ShouldBe(WebhookEvents.AbsenceUpdated);
        published.Absence.StartDate.ShouldBe(new DateOnly(2026, 6, 1));
        published.Absence.Type.ShouldBe(AbsenceType.Sick);
    }

    [Fact]
    public async Task DeleteAsync_PublishesAnAbsenceDeletedEventDescribingTheRemovedAbsence()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5), AbsenceType.Vacation, null));
        webhooks.Published.Clear();

        await sut.DeleteAsync(person, created.Id);

        var published = webhooks.Published.ShouldHaveSingleItem();
        published.Event.ShouldBe(WebhookEvents.AbsenceDeleted);
        published.Absence.Id.ShouldBe(created.Id);
        published.Absence.StartDate.ShouldBe(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public async Task CreateAsync_WhenTheAbsenceIsRejected_PublishesNothing()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5), AbsenceType.Vacation, null));
        webhooks.Published.Clear();

        await Should.ThrowAsync<InvalidOperationException>(() => sut.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 5, 3), new DateOnly(2026, 5, 4), AbsenceType.Sick, null)));

        webhooks.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetOverlappingAsync_ReturnsTheViewersOwnNote()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), person.Id);

        result.Absences.ShouldHaveSingleItem().Note.ShouldBe("Flu");
    }

    // The team can see that someone is off and what kind of absence it is. Why is between
    // them and the app.
    [Fact]
    public async Task GetOverlappingAsync_WithholdsEveryoneElsesNote()
    {
        var author = CreatePerson("jdoe", "#111111");
        var viewer = CreatePerson("asmith", "#222222");
        await sut.CreateAsync(author, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), viewer.Id);

        var absence = result.Absences.ShouldHaveSingleItem();
        absence.Note.ShouldBeNull();
        absence.Type.ShouldBe(AbsenceType.Sick);
    }

    [Fact]
    public async Task GetOverlappingAsync_WhenTheViewerHasNoProfileYet_WithholdsEveryNote()
    {
        var author = CreatePerson();
        await sut.CreateAsync(author, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), viewerPersonId: null);

        result.Absences.ShouldHaveSingleItem().Note.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_LogsWhatWentWithoutTheNote()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(
            person, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));

        await sut.DeleteAsync(person, created.Id);

        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditActions.AbsenceDeleted);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Sick, 2026-03-02 → 2026-03-06");
        entry.Details.ShouldNotContain("Flu");
    }
}
