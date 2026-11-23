using System.Security.Claims;

using Api.Configuration;
using Api.Data.Models;
using Api.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class AuditLogTests : SqliteTestBase
{
    private static readonly AuthOptions Auth = new();

    private AuditLog CreateSut(string? signedInAs = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        if (signedInAs is not null)
            accessor.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(Auth.ClaimTypes.UserName, signedInAs)], "Test"));

        return new AuditLog(Db, accessor, Options.Create(Auth));
    }

    [Fact]
    [Trait("Spec", "AUD-1")]
    public async Task RecordAsync_AttributesTheEntryToWhoeverIsSignedIn()
    {
        var sut = CreateSut("jdoe");

        await sut.RecordAsync(AuditActions.PersonDeleted, "Jane D.", "jdoe, CH-ZH");

        var entry = Db.AuditEntries.Single();
        entry.Actor.ShouldBe("jdoe");
        entry.Action.ShouldBe(AuditActions.PersonDeleted);
        entry.Target.ShouldBe("Jane D.");
        entry.Details.ShouldBe("jdoe, CH-ZH");
        entry.OccurredAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
    }

    // The scheduled jobs have nobody behind them, and a missing username must not be able
    // to fail the write and take the action it is recording down with it.
    [Fact]
    [Trait("Spec", "AUD-1")]
    public async Task RecordAsync_WhenNobodyIsSignedIn_AttributesTheEntryToTheSystem()
    {
        var sut = CreateSut();

        await sut.RecordAsync(AuditActions.BankHolidaysSynced, "2026, all regions");

        Db.AuditEntries.Single().Actor.ShouldBe(AuditLog.SystemActor);
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_ReturnsNewestFirstAndReportsTheWholeTotal()
    {
        var sut = CreateSut("jdoe");
        for (var i = 1; i <= 3; i++)
            await sut.RecordAsync(AuditActions.PersonUpdated, $"person {i}");

        var result = await sut.ReadAsync(limit: 2, offset: 0);

        result.Entries.Select(e => e.Target).ShouldBe(["person 3", "person 2"]);
        result.Total.ShouldBe(3);
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_SkipsTheEntriesAlreadyRead()
    {
        var sut = CreateSut("jdoe");
        for (var i = 1; i <= 3; i++)
            await sut.RecordAsync(AuditActions.PersonUpdated, $"person {i}");

        var result = await sut.ReadAsync(limit: 2, offset: 2);

        result.Entries.ShouldHaveSingleItem().Target.ShouldBe("person 1");
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_FiltersBySearch_MatchingActorSubstringCaseInsensitively()
    {
        var sut = CreateSut();
        Db.AuditEntries.AddRange(
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "alice",
                Action = AuditActions.PersonUpdated,
                Target = "t1",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "bob",
                Action = AuditActions.PersonUpdated,
                Target = "t2",
            });
        await Db.SaveChangesAsync();

        var result = await sut.ReadAsync(limit: 25, offset: 0, search: "ALI");

        result.Entries.ShouldHaveSingleItem().Actor.ShouldBe("alice");
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_SearchTreatsUnderscoreLiterally_NotAsAOneCharacterWildcard()
    {
        var sut = CreateSut();
        Db.AuditEntries.AddRange(
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "j_doe",
                Action = AuditActions.PersonUpdated,
                Target = "t1",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "jxdoe",
                Action = AuditActions.PersonUpdated,
                Target = "t2",
            });
        await Db.SaveChangesAsync();

        var result = await sut.ReadAsync(limit: 25, offset: 0, search: "j_doe");

        result.Entries.ShouldHaveSingleItem().Actor.ShouldBe("j_doe");
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_FiltersByAction()
    {
        var sut = CreateSut();
        Db.AuditEntries.AddRange(
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "alice",
                Action = AuditActions.PersonUpdated,
                Target = "t1",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "alice",
                Action = AuditActions.PersonDeleted,
                Target = "t2",
            });
        await Db.SaveChangesAsync();

        var result = await sut.ReadAsync(limit: 25, offset: 0, action: AuditActions.PersonDeleted);

        result.Entries.ShouldHaveSingleItem().Action.ShouldBe(AuditActions.PersonDeleted);
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_FiltersByTimeRange_InclusiveFromExclusiveTo()
    {
        var sut = CreateSut();
        var day1 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var day3 = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        Db.AuditEntries.AddRange(
            new AuditEntry
            {
                OccurredAt = day1,
                Actor = "alice",
                Action = AuditActions.PersonUpdated,
                Target = "day1",
            },
            new AuditEntry
            {
                OccurredAt = day2,
                Actor = "alice",
                Action = AuditActions.PersonUpdated,
                Target = "day2",
            },
            new AuditEntry
            {
                OccurredAt = day3,
                Actor = "alice",
                Action = AuditActions.PersonUpdated,
                Target = "day3",
            });
        await Db.SaveChangesAsync();

        // from is inclusive (day2 itself counts), to is exclusive (day3 itself does not) -
        // together they cover exactly the single calendar day the caller computed them for.
        var result = await sut.ReadAsync(limit: 25, offset: 0, from: day2, to: day3);

        result.Entries.ShouldHaveSingleItem().Target.ShouldBe("day2");
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_SortsByActorAscending()
    {
        var sut = CreateSut();
        Db.AuditEntries.AddRange(
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "carl",
                Action = AuditActions.PersonUpdated,
                Target = "t1",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "alice",
                Action = AuditActions.PersonUpdated,
                Target = "t2",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "bob",
                Action = AuditActions.PersonUpdated,
                Target = "t3",
            });
        await Db.SaveChangesAsync();

        var result = await sut.ReadAsync(
            limit: 25, offset: 0, sortBy: AuditSortColumn.Actor, descending: false);

        result.Entries.Select(e => e.Actor).ShouldBe(["alice", "bob", "carl"]);
    }

    [Fact]
    [Trait("Spec", "AUD-4")]
    public async Task ReadAsync_SortsByActionDescending()
    {
        var sut = CreateSut();
        Db.AuditEntries.AddRange(
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "a",
                Action = AuditActions.PersonCreated,
                Target = "t1",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "a",
                Action = AuditActions.PersonDeleted,
                Target = "t2",
            },
            new AuditEntry
            {
                OccurredAt = DateTime.UtcNow,
                Actor = "a",
                Action = AuditActions.PersonUpdated,
                Target = "t3",
            });
        await Db.SaveChangesAsync();

        var result = await sut.ReadAsync(
            limit: 25, offset: 0, sortBy: AuditSortColumn.Action, descending: true);

        result.Entries.Select(e => e.Action).ShouldBe(
            [AuditActions.PersonUpdated, AuditActions.PersonDeleted, AuditActions.PersonCreated]);
    }
}
