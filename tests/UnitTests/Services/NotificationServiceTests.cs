using System.Globalization;

using Api.Data.Models;
using Api.Services;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class NotificationServiceTests : SqliteTestBase
{
    private readonly NotificationService sut;

    public NotificationServiceTests()
    {
        sut = new NotificationService(Db);
    }

    private Person CreatePerson(string username = "jdoe")
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    private static Absence NewAbsence(int personId, string start, string end, AbsenceType type = AbsenceType.Vacation) => new()
    {
        PersonId = personId,
        StartDate = DateOnly.Parse(start, CultureInfo.InvariantCulture),
        EndDate = DateOnly.Parse(end, CultureInfo.InvariantCulture),
        Type = type,
        Status = AbsenceStatus.Approved,
        CreatedBy = "jdoe",
    };

    [Fact]
    public async Task NotifyAsync_RecordsANotificationForTheAbsencesOwner()
    {
        var person = CreatePerson();
        var absence = NewAbsence(person.Id, "2026-03-02", "2026-03-06", AbsenceType.Other);

        await sut.NotifyAsync(absence, NotificationType.AbsenceRejected, "Not enough coverage.");

        var result = await sut.GetForPersonAsync(person.Id, includePendingApprovals: false);
        var notification = result.Notifications.ShouldHaveSingleItem();
        notification.Type.ShouldBe(NotificationType.AbsenceRejected);
        notification.AbsenceType.ShouldBe(AbsenceType.Other);
        notification.StartDate.ShouldBe(new DateOnly(2026, 3, 2));
        notification.Note.ShouldBe("Not enough coverage.");
        notification.ReadAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetForPersonAsync_ReturnsNewestFirstAndCountsWhatIsUnread()
    {
        var person = CreatePerson();
        await sut.NotifyAsync(
            NewAbsence(person.Id, "2026-01-05", "2026-01-06"), NotificationType.AbsenceApproved, null);
        await sut.NotifyAsync(
            NewAbsence(person.Id, "2026-02-05", "2026-02-06"), NotificationType.AbsenceRejected, "No.");

        var result = await sut.GetForPersonAsync(person.Id, includePendingApprovals: false);

        result.Notifications.Select(n => n.Type)
            .ShouldBe([NotificationType.AbsenceRejected, NotificationType.AbsenceApproved]);
        result.UnreadCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetForPersonAsync_OnlyReturnsThatPersonsNotifications()
    {
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
        await sut.NotifyAsync(
            NewAbsence(alice.Id, "2026-01-05", "2026-01-06"), NotificationType.AbsenceApproved, null);

        var result = await sut.GetForPersonAsync(bob.Id, includePendingApprovals: false);

        result.Notifications.ShouldBeEmpty();
        result.UnreadCount.ShouldBe(0);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksOnlyThatPersonsUnreadNotifications()
    {
        var alice = CreatePerson("alice");
        var bob = CreatePerson("bob");
        await sut.NotifyAsync(
            NewAbsence(alice.Id, "2026-01-05", "2026-01-06"), NotificationType.AbsenceApproved, null);
        await sut.NotifyAsync(
            NewAbsence(bob.Id, "2026-01-05", "2026-01-06"), NotificationType.AbsenceApproved, null);

        await sut.MarkAllReadAsync(alice.Id);

        var aliceResult = await sut.GetForPersonAsync(alice.Id, includePendingApprovals: false);
        aliceResult.UnreadCount.ShouldBe(0);
        aliceResult.Notifications.ShouldHaveSingleItem().ReadAt.ShouldNotBeNull();

        var bobResult = await sut.GetForPersonAsync(bob.Id, includePendingApprovals: false);
        bobResult.UnreadCount.ShouldBe(1);
    }

    private int pendingCreationOrder;

    private void AddPendingAbsence(int personId, string start, string end, AbsenceType type = AbsenceType.Vacation)
    {
        Db.Absences.Add(new Absence
        {
            PersonId = personId,
            StartDate = DateOnly.Parse(start, CultureInfo.InvariantCulture),
            EndDate = DateOnly.Parse(end, CultureInfo.InvariantCulture),
            Type = type,
            Status = AbsenceStatus.Pending,
            // Each call a tick later than the last, so ordering-by-CreatedAt tests don't
            // depend on every row otherwise landing at the same default DateTime.
            CreatedAt = DateTime.UtcNow.AddSeconds(++pendingCreationOrder),
            CreatedBy = "jdoe",
        });
        Db.SaveChanges();
    }

    [Fact]
    public async Task GetForPersonAsync_WhenNotIncludingPendingApprovals_ReturnsNoneEvenIfSomeExist()
    {
        var approver = CreatePerson("approver");
        var requester = CreatePerson("jdoe");
        AddPendingAbsence(requester.Id, "2026-03-02", "2026-03-06");

        var result = await sut.GetForPersonAsync(approver.Id, includePendingApprovals: false);

        result.PendingApprovals.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetForPersonAsync_WhenIncludingPendingApprovals_ReturnsEveryoneElsesPendingRequests()
    {
        var approver = CreatePerson("approver");
        var requester = CreatePerson("jdoe");
        AddPendingAbsence(requester.Id, "2026-03-02", "2026-03-06", AbsenceType.Other);

        var result = await sut.GetForPersonAsync(approver.Id, includePendingApprovals: true);

        var pending = result.PendingApprovals.ShouldHaveSingleItem();
        pending.PersonName.ShouldBe(requester.DisplayName);
        pending.Type.ShouldBe(AbsenceType.Other);
        pending.StartDate.ShouldBe(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public async Task GetForPersonAsync_ExcludesTheViewersOwnPendingRequest()
    {
        var approver = CreatePerson("approver");
        AddPendingAbsence(approver.Id, "2026-03-02", "2026-03-06");

        var result = await sut.GetForPersonAsync(approver.Id, includePendingApprovals: true);

        result.PendingApprovals.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetForPersonAsync_ExcludesApprovedAndRejectedAbsences()
    {
        var approver = CreatePerson("approver");
        var requester = CreatePerson("jdoe");
        Db.Absences.AddRange(
            NewAbsence(requester.Id, "2026-01-05", "2026-01-06"),
            new Absence
            {
                PersonId = requester.Id,
                StartDate = new DateOnly(2026, 2, 5),
                EndDate = new DateOnly(2026, 2, 6),
                Type = AbsenceType.Vacation,
                Status = AbsenceStatus.Rejected,
                CreatedBy = "jdoe",
            });
        Db.SaveChanges();

        var result = await sut.GetForPersonAsync(approver.Id, includePendingApprovals: true);

        result.PendingApprovals.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetForPersonAsync_OrdersPendingApprovalsByWhenTheyWereFiled_OldestFirst()
    {
        var approver = CreatePerson("approver");
        var requester = CreatePerson("jdoe");
        // Filed in this order regardless of which absence dates they're for - it's the
        // filing order the queue is meant to be worked down in, same as the approvals page.
        AddPendingAbsence(requester.Id, "2026-05-01", "2026-05-05");
        AddPendingAbsence(requester.Id, "2026-01-01", "2026-01-05");

        var result = await sut.GetForPersonAsync(approver.Id, includePendingApprovals: true);

        result.PendingApprovals.Select(p => p.StartDate)
            .ShouldBe([new DateOnly(2026, 5, 1), new DateOnly(2026, 1, 1)]);
    }
}
