using Api.Configuration;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Options;

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
    private readonly RecordingNotificationService notifications = new();
    private readonly RecordingEmailSender emails = new();

    // Approvals off by default, matching every deployment until it opts in: the bulk of
    // this file exercises today's behavior. Tests for the approval workflow itself build
    // their own service with approvals turned on, see ApprovalsEnabledSut().
    private readonly AbsenceService sut;

    public AbsenceServiceTests()
    {
        sut = ApprovalsEnabledSut(enabled: false);
    }

    private AbsenceService ApprovalsEnabledSut(bool enabled) => new(
        Db,
        webhooks,
        audit,
        notifications,
        emails,
        Options.Create(new AbsenceApprovalOptions { Enabled = enabled }),
        Options.Create(new BrandingOptions { AppName = "Foo Calendar" }),
        Options.Create(new EmailOptions()));

    private Person CreatePerson(
        string username = "jdoe",
        string email = null,
        bool emailOnAbsenceReviewed = false,
        bool emailOnNewAbsenceRequest = false)
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            Email = email,
            EmailOnAbsenceReviewed = emailOnAbsenceReviewed,
            EmailOnNewAbsenceRequest = emailOnNewAbsenceRequest,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    private static CreateAbsenceRequest Request(
        string start, string end, AbsenceType type = AbsenceType.Vacation, string note = null,
        string startTime = null, string endTime = null) =>
        new(
            DateOnly.Parse(start), DateOnly.Parse(end), type, note,
            startTime is null ? null : TimeOnly.Parse(startTime),
            endTime is null ? null : TimeOnly.Parse(endTime));

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
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
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
        var owner = CreatePerson("alice");
        var intruder = CreatePerson("bob");
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
        var owner = CreatePerson("alice");
        var intruder = CreatePerson("bob");
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
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
        // Mon 2026-06-01 through Fri 2026-06-05: 5 consecutive working days.
        await sut.CreateAsync(alice, Request("2026-06-01", "2026-06-05"));

        var stats = await sut.GetAllStatsAsync(2026);

        stats.Stats.Single(s => s.PersonId == alice.Id).VacationDays.ShouldBe(5);
        stats.Stats.Single(s => s.PersonId == bob.Id).VacationDays.ShouldBe(0);
    }

    [Fact]
    public async Task GetOverlappingAsync_ReturnsApprovedAndPendingAbsencesWithinRange()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-07-06", "2026-07-10"));
        Db.Absences.Add(new Absence
        {
            PersonId = person.Id,
            StartDate = new DateOnly(2026, 8, 6),
            EndDate = new DateOnly(2026, 8, 10),
            Type = AbsenceType.Sick,
            Status = AbsenceStatus.Pending,
            CreatedBy = person.Username,
        });
        Db.SaveChanges();

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31), person.Id);

        result.Absences.Count.ShouldBe(2);
        result.Absences.ShouldContain(a => a.Type == AbsenceType.Vacation && a.Status == AbsenceStatus.Approved);
        result.Absences.ShouldContain(a => a.Type == AbsenceType.Sick && a.Status == AbsenceStatus.Pending);
    }

    [Fact]
    public async Task GetOverlappingAsync_ExcludesRejectedAbsences()
    {
        var person = CreatePerson();
        Db.Absences.Add(new Absence
        {
            PersonId = person.Id,
            StartDate = new DateOnly(2026, 7, 6),
            EndDate = new DateOnly(2026, 7, 10),
            Type = AbsenceType.Sick,
            Status = AbsenceStatus.Rejected,
            CreatedBy = person.Username,
        });
        Db.SaveChanges();

        var result = await sut.GetOverlappingAsync(
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), person.Id);

        result.Absences.ShouldBeEmpty();
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
    public async Task CreateAsync_LogsWhatWasFiledWithoutTheNote()
    {
        var person = CreatePerson();

        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceCreated);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Sick, 2026-03-02 → 2026-03-06");
        entry.Details.ShouldNotContain("Flu");
    }

    [Fact]
    public async Task UpdateAsync_LogsWhatChangedWithoutTheNote()
    {
        var person = CreatePerson();
        var created = await sut.CreateAsync(
            person, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));

        await sut.UpdateAsync(
            person, created.Id, Request("2026-04-02", "2026-04-06", AbsenceType.Vacation, "Trip"));

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceUpdated);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Vacation, 2026-04-02 → 2026-04-06");
        entry.Details.ShouldNotContain("Trip");
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
        var author = CreatePerson("jdoe");
        var viewer = CreatePerson("asmith");
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

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceDeleted);
        entry.Target.ShouldBe(person.DisplayName);
        entry.Details.ShouldBe("Sick, 2026-03-02 → 2026-03-06");
        entry.Details.ShouldNotContain("Flu");
    }

    [Fact]
    public async Task CreateAsync_WhenApprovalsAreOff_IsApprovedImmediately()
    {
        var person = CreatePerson();

        var created = await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        created.Status.ShouldBe(AbsenceStatus.Approved);
    }

    [Fact]
    public async Task CreateAsync_WhenApprovalsAreOn_StartsPending()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();

        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        created.Status.ShouldBe(AbsenceStatus.Pending);
    }

    [Fact]
    public async Task CreateAsync_WhenApprovalsAreOn_StillConflictsWithAPendingAbsence()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => approvalSut.CreateAsync(person, Request("2026-03-05", "2026-03-10")));
    }

    [Fact]
    public async Task UpdateAsync_WhenApprovalsAreOnAndTheAbsenceWasApproved_ResetsItToPending()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        await approvalSut.ApproveAsync(CreatePerson("reviewer"), created.Id);

        var updated = await approvalSut.UpdateAsync(
            person, created.Id, Request("2026-04-01", "2026-04-05"));

        updated.Status.ShouldBe(AbsenceStatus.Pending);
    }

    [Fact]
    public async Task UpdateAsync_WhenApprovalsAreOnAndTheAbsenceIsStillPending_StaysPending()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        var updated = await approvalSut.UpdateAsync(
            person, created.Id, Request("2026-04-01", "2026-04-05"));

        updated.Status.ShouldBe(AbsenceStatus.Pending);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheAbsenceWasRejected_Throws()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        await approvalSut.RejectAsync(CreatePerson("reviewer"), created.Id, note: null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => approvalSut.UpdateAsync(person, created.Id, Request("2026-04-01", "2026-04-05")));
    }

    [Fact]
    public async Task ApproveAsync_WhenTheReviewerIsTheAbsencesOwner_Throws()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => approvalSut.ApproveAsync(person, created.Id));
    }

    [Fact]
    public async Task ApproveAsync_WhenApprovalsAreLaterDisabled_StillResolvesALeftoverPendingRequest()
    {
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var created = await ApprovalsEnabledSut(enabled: true).CreateAsync(
            person, Request("2026-03-02", "2026-03-06"));

        var approved = await ApprovalsEnabledSut(enabled: false).ApproveAsync(reviewer, created.Id);

        approved.Status.ShouldBe(AbsenceStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheAbsenceIsNotPending_Throws()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        await approvalSut.ApproveAsync(reviewer, created.Id);

        await Should.ThrowAsync<InvalidOperationException>(
            () => approvalSut.ApproveAsync(reviewer, created.Id));
    }

    [Fact]
    public async Task ApproveAsync_ApprovesLogsNotifiesAndPublishes()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        webhooks.Published.Clear();

        var approved = await approvalSut.ApproveAsync(reviewer, created.Id);

        approved.Status.ShouldBe(AbsenceStatus.Approved);

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceApproved);
        entry.Target.ShouldBe(person.DisplayName);

        var notified = notifications.Notified.ShouldHaveSingleItem();
        notified.PersonId.ShouldBe(person.Id);
        notified.Type.ShouldBe(NotificationType.AbsenceApproved);

        var published = webhooks.Published.ShouldHaveSingleItem();
        published.Event.ShouldBe(WebhookEvents.AbsenceApproved);
        published.Absence.Status.ShouldBe(AbsenceStatus.Approved);
        // The reviewer approved it, not the requester whose name is on the absence itself.
        published.Actor.ShouldBe(reviewer.Username);
    }

    [Fact]
    public async Task RejectAsync_WhenTheReviewerIsTheAbsencesOwner_Throws()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => approvalSut.RejectAsync(person, created.Id, note: null));
    }

    [Fact]
    public async Task RejectAsync_WhenTheAbsenceIsNotPending_Throws()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        await approvalSut.RejectAsync(reviewer, created.Id, note: null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => approvalSut.RejectAsync(reviewer, created.Id, note: null));
    }

    [Fact]
    public async Task RejectAsync_RejectsLogsNotifiesAndPublishesWithoutTheReviewNoteInTheAudit()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        webhooks.Published.Clear();

        var rejected = await approvalSut.RejectAsync(reviewer, created.Id, "Not enough coverage that week.");

        rejected.Status.ShouldBe(AbsenceStatus.Rejected);

        var entry = audit.Entries.Single(e => e.Action == AuditActions.AbsenceRejected);
        entry.Details.ShouldNotContain("Not enough coverage");

        var notified = notifications.Notified.ShouldHaveSingleItem();
        notified.Type.ShouldBe(NotificationType.AbsenceRejected);
        notified.Note.ShouldBe("Not enough coverage that week.");

        var published = webhooks.Published.ShouldHaveSingleItem();
        published.Event.ShouldBe(WebhookEvents.AbsenceRejected);
        published.Absence.ReviewNote.ShouldBe("Not enough coverage that week.");
        published.Actor.ShouldBe(reviewer.Username);
    }

    [Fact]
    public async Task RejectAsync_RemovesTheAbsenceFromTheCalendar()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await approvalSut.RejectAsync(reviewer, created.Id, note: null);

        var result = await approvalSut.GetOverlappingAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), person.Id);
        result.Absences.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPendingAsync_WhenApprovalsAreOff_ReturnsNothingByDefault()
    {
        // Approvals off means nothing is ever created Pending, not that the queue is
        // unreachable: a leftover from before is still returned, see the test below.
        (await sut.GetPendingAsync()).Absences.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPendingAsync_WhenApprovalsAreLaterDisabled_StillReturnsALeftoverPendingRequest()
    {
        var person = CreatePerson();
        var created = await ApprovalsEnabledSut(enabled: true).CreateAsync(
            person, Request("2026-03-02", "2026-03-06"));

        var result = await ApprovalsEnabledSut(enabled: false).GetPendingAsync();

        result.Absences.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyPendingAbsencesWithTheirNote()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson();
        var reviewer = CreatePerson("reviewer");
        var pending = await approvalSut.CreateAsync(
            person, Request("2026-03-02", "2026-03-06", AbsenceType.Sick, "Flu"));
        var approved = await approvalSut.CreateAsync(person, Request("2026-05-01", "2026-05-05"));
        await approvalSut.ApproveAsync(reviewer, approved.Id);

        var result = await approvalSut.GetPendingAsync();

        var absence = result.Absences.ShouldHaveSingleItem();
        absence.Id.ShouldBe(pending.Id);
        absence.Note.ShouldBe("Flu");
    }

    [Fact]
    public async Task ApproveAsync_WhenTheOwnerOptedIn_EmailsThem()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson(email: "jdoe@example.test", emailOnAbsenceReviewed: true);
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await approvalSut.ApproveAsync(reviewer, created.Id);

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.ToAddress.ShouldBe("jdoe@example.test");
    }

    [Fact]
    public async Task ApproveAsync_WhenTheOwnerDidNotOptIn_SendsNoEmail()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson(email: "jdoe@example.test", emailOnAbsenceReviewed: false);
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await approvalSut.ApproveAsync(reviewer, created.Id);

        emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenTheOwnerOptedInButHasNoEmailAddress_SendsNoEmail()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson(email: null, emailOnAbsenceReviewed: true);
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await approvalSut.ApproveAsync(reviewer, created.Id);

        emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectAsync_WhenTheOwnerOptedIn_EmailsThemWithTheReviewNote()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var person = CreatePerson(email: "jdoe@example.test", emailOnAbsenceReviewed: true);
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        await approvalSut.RejectAsync(reviewer, created.Id, "Not enough coverage that week.");

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.ToAddress.ShouldBe("jdoe@example.test");
        sent.HtmlBody.ShouldContain("Not enough coverage that week.");
    }

    [Fact]
    public async Task CreateAsync_WhenApprovalsAreOnAndAnAbsenceLandsPending_EmailsEveryOptedInApprover()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var approver = CreatePerson("approver", email: "approver@example.test", emailOnNewAbsenceRequest: true);
        var uninterested = CreatePerson("bystander", email: "bystander@example.test");
        var person = CreatePerson("jdoe");

        await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.ToAddress.ShouldBe(approver.Email);
        uninterested.Email.ShouldNotBeNull(); // sanity: they had an address, just didn't opt in
    }

    [Fact]
    public async Task CreateAsync_WhenApprovalsAreOff_NeverEmailsApprovers()
    {
        // Off means the absence is Approved on arrival, so there is nothing pending for an
        // approver to be told about, opted in or not.
        CreatePerson("approver", email: "approver@example.test", emailOnNewAbsenceRequest: true);
        var person = CreatePerson("jdoe");

        await sut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));

        emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenAnApprovedAbsenceIsResetToPending_EmailsOptedInApprovers()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var approver = CreatePerson("approver", email: "approver@example.test", emailOnNewAbsenceRequest: true);
        var person = CreatePerson("jdoe");
        var reviewer = CreatePerson("reviewer");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        await approvalSut.ApproveAsync(reviewer, created.Id);
        emails.Sent.Clear();

        await approvalSut.UpdateAsync(person, created.Id, Request("2026-04-01", "2026-04-05"));

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.ToAddress.ShouldBe(approver.Email);
    }

    [Fact]
    public async Task UpdateAsync_WhenAlreadyPendingAndEditedAgain_DoesNotReEmailApprovers()
    {
        var approvalSut = ApprovalsEnabledSut(enabled: true);
        var approver = CreatePerson("approver", email: "approver@example.test", emailOnNewAbsenceRequest: true);
        var person = CreatePerson("jdoe");
        var created = await approvalSut.CreateAsync(person, Request("2026-03-02", "2026-03-06"));
        emails.Sent.Clear();

        await approvalSut.UpdateAsync(person, created.Id, Request("2026-04-01", "2026-04-05"));

        emails.Sent.ShouldBeEmpty();
        approver.Email.ShouldNotBeNull(); // sanity: they were still a valid recipient, just not re-notified
    }

    [Fact]
    public async Task CreateAsync_TwoAppointmentsSameDayWithOverlappingTimes_BothSucceed()
    {
        var person = CreatePerson();

        var first = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "11:00"));
        var second = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "10:00", endTime: "12:00"));

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public async Task CreateAsync_AppointmentOnADayWithAnExistingVacation_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-08-20", "2026-08-20"));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00")));
    }

    [Fact]
    public async Task CreateAsync_VacationOnADayWithAnExistingAppointment_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-08-20", "2026-08-20")));
    }

    [Fact]
    public async Task CreateAsync_AppointmentsOnDifferentDays_BothSucceed()
    {
        var person = CreatePerson();

        var first = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));
        var second = await sut.CreateAsync(
            person, Request("2026-08-21", "2026-08-21", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public async Task UpdateAsync_AppointmentOnADayWithAnExistingVacation_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-08-20", "2026-08-20"));
        var toEdit = await sut.CreateAsync(
            person, Request("2026-08-25", "2026-08-25", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.UpdateAsync(
            person, toEdit.Id,
            Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00")));
    }

    [Fact]
    public async Task UpdateAsync_TwoAppointmentsSameDay_DoesNotConflictWithItself()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));
        var toEdit = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "13:00", endTime: "14:00"));

        var updated = await sut.UpdateAsync(
            person, toEdit.Id,
            Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:30", endTime: "10:30"));

        updated.Type.ShouldBe(AbsenceType.Appointment);
    }

    [Fact]
    public async Task CreateAsync_TimeBasedOtherAndAppointmentSameDay_BothSucceed()
    {
        var person = CreatePerson();

        var appointment = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Appointment, startTime: "09:00", endTime: "10:00"));
        var timeBasedOther = await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "13:00", endTime: "14:00"));

        appointment.Id.ShouldNotBe(timeBasedOther.Id);
    }

    [Fact]
    public async Task CreateAsync_TimeBasedOtherOnADayWithAnExistingVacation_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(person, Request("2026-08-20", "2026-08-20"));

        await Should.ThrowAsync<InvalidOperationException>(() => sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "09:00", endTime: "10:00")));
    }

    [Fact]
    public async Task CreateAsync_WholeDayOtherOnADayWithAnExistingTimeBasedOther_Throws()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "09:00", endTime: "10:00"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(person, Request("2026-08-20", "2026-08-20", AbsenceType.Other)));
    }

    [Fact]
    public async Task GetStatsAsync_TimeBasedOther_DoesNotCountTowardOtherDays()
    {
        var person = CreatePerson();
        await sut.CreateAsync(
            person, Request("2026-08-20", "2026-08-20", AbsenceType.Other, startTime: "09:00", endTime: "10:00"));

        var stats = await sut.GetStatsAsync(person.Id, 2026);

        stats.OtherDays.ShouldBe(0);
    }
}
