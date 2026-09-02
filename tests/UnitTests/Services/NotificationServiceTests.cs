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
        StartDate = DateOnly.Parse(start),
        EndDate = DateOnly.Parse(end),
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

        var result = await sut.GetForPersonAsync(person.Id);
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

        var result = await sut.GetForPersonAsync(person.Id);

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

        var result = await sut.GetForPersonAsync(bob.Id);

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

        var aliceResult = await sut.GetForPersonAsync(alice.Id);
        aliceResult.UnreadCount.ShouldBe(0);
        aliceResult.Notifications.ShouldHaveSingleItem().ReadAt.ShouldNotBeNull();

        var bobResult = await sut.GetForPersonAsync(bob.Id);
        bobResult.UnreadCount.ShouldBe(1);
    }
}
