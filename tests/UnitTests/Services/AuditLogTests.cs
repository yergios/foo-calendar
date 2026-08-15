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

    private AuditLog CreateSut(string signedInAs = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        if (signedInAs is not null)
            accessor.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(Auth.ClaimTypes.UserName, signedInAs)], "Test"));

        return new AuditLog(Db, accessor, Options.Create(Auth));
    }

    [Fact]
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
    public async Task RecordAsync_WhenNobodyIsSignedIn_AttributesTheEntryToTheSystem()
    {
        var sut = CreateSut();

        await sut.RecordAsync(AuditActions.BankHolidaysSynced, "2026, all regions");

        Db.AuditEntries.Single().Actor.ShouldBe(AuditLog.SystemActor);
    }

    [Fact]
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
    public async Task ReadAsync_SkipsTheEntriesAlreadyRead()
    {
        var sut = CreateSut("jdoe");
        for (var i = 1; i <= 3; i++)
            await sut.RecordAsync(AuditActions.PersonUpdated, $"person {i}");

        var result = await sut.ReadAsync(limit: 2, offset: 2);

        result.Entries.ShouldHaveSingleItem().Target.ShouldBe("person 1");
    }
}
