using Api.Configuration;
using Api.Data.Models;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class WeeklyDigestServiceTests : SqliteTestBase
{
    private readonly RecordingTeamRegionService teamRegions = new() { InUse = ["CH-ZH"] };
    private readonly RecordingEmailSender emails = new();
    private readonly WeeklyDigestService sut;

    public WeeklyDigestServiceTests()
    {
        var bankHolidays = new BankHolidayService(
            Db, new DisabledHolidaySource(), new RecordingAuditLog(), NullLogger<BankHolidayService>.Instance);

        sut = new WeeklyDigestService(
            Db,
            bankHolidays,
            teamRegions,
            emails,
            Options.Create(new EmailOptions()),
            Options.Create(new TeamOptions { HomeTimeZone = "UTC" }));
    }

    // UTC, matching the team's HomeTimeZone configured above: SendAsync computes "this
    // week" in that zone, not the test host's local time, so this has to agree with it
    // rather than with DateTime.Now to stay correct regardless of where tests run.
    private static (DateOnly Start, DateOnly End) ThisWeek()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        return (start, start.AddDays(6));
    }

    private Person CreatePerson(string username, bool emailOnWeeklyDigest, string email = "person@example.test")
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            Email = email,
            EmailOnWeeklyDigest = emailOnWeeklyDigest,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    [Fact]
    public async Task SendAsync_WhenNobodyOptedIn_SendsNothing()
    {
        CreatePerson("jdoe", emailOnWeeklyDigest: false);

        await sut.SendAsync();

        emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_EmailsOnlyThePeopleWhoOptedIn()
    {
        var subscriber = CreatePerson("subscriber", emailOnWeeklyDigest: true, email: "subscriber@example.test");
        CreatePerson("bystander", emailOnWeeklyDigest: false, email: "bystander@example.test");

        await sut.SendAsync();

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.ToAddress.ShouldBe(subscriber.Email);
    }

    [Fact]
    public async Task SendAsync_SkipsAnOptedInPersonWithNoEmailAddress()
    {
        CreatePerson("subscriber", emailOnWeeklyDigest: true, email: null);

        await sut.SendAsync();

        emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_SendsTheIdenticalBodyToEveryRecipient()
    {
        CreatePerson("alice", emailOnWeeklyDigest: true, email: "alice@example.test");
        CreatePerson("bob", emailOnWeeklyDigest: true, email: "bob@example.test");
        var (weekStart, _) = ThisWeek();
        Db.Absences.Add(new Absence
        {
            PersonId = CreatePerson("carol", emailOnWeeklyDigest: false).Id,
            StartDate = weekStart,
            EndDate = weekStart,
            Type = AbsenceType.Vacation,
            Status = AbsenceStatus.Approved,
            CreatedBy = "carol",
        });
        Db.SaveChanges();

        await sut.SendAsync();

        emails.Sent.Count.ShouldBe(2);
        emails.Sent[0].Subject.ShouldBe(emails.Sent[1].Subject);
        emails.Sent[0].HtmlBody.ShouldBe(emails.Sent[1].HtmlBody);
        emails.Sent[0].HtmlBody.ShouldContain("carol");
    }

    [Fact]
    public async Task SendAsync_IncludesAnApprovedAbsenceOverlappingThisWeekButExcludesPending()
    {
        CreatePerson("subscriber", emailOnWeeklyDigest: true, email: "subscriber@example.test");
        var (weekStart, _) = ThisWeek();

        var approvedOwner = CreatePerson("approved-owner", emailOnWeeklyDigest: false);
        Db.Absences.Add(new Absence
        {
            PersonId = approvedOwner.Id,
            StartDate = weekStart,
            EndDate = weekStart,
            Type = AbsenceType.Vacation,
            Status = AbsenceStatus.Approved,
            CreatedBy = approvedOwner.Username,
        });

        var pendingOwner = CreatePerson("pending-owner", emailOnWeeklyDigest: false);
        Db.Absences.Add(new Absence
        {
            PersonId = pendingOwner.Id,
            StartDate = weekStart,
            EndDate = weekStart,
            Type = AbsenceType.Other,
            Status = AbsenceStatus.Pending,
            CreatedBy = pendingOwner.Username,
        });
        Db.SaveChanges();

        await sut.SendAsync();

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.HtmlBody.ShouldContain("approved-owner");
        sent.HtmlBody.ShouldNotContain("pending-owner");
    }

    [Fact]
    public async Task SendAsync_IncludesABankHolidayFallingInThisWeek()
    {
        CreatePerson("subscriber", emailOnWeeklyDigest: true, email: "subscriber@example.test");
        var (weekStart, _) = ThisWeek();

        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH-ZH",
            Date = weekStart,
            Name = "Test Holiday",
            CreatedBy = "manual",
        });
        Db.SaveChanges();

        await sut.SendAsync();

        var sent = emails.Sent.ShouldHaveSingleItem();
        sent.HtmlBody.ShouldContain("Test Holiday");
    }
}
