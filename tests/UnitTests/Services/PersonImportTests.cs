using Api.Data.Models;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class PersonImportTests : SqliteTestBase
{
    private const string Actor = "admin";

    private readonly RecordingTeamRegionService teamRegions = new();

    private readonly RecordingAuditLog audit = new();

    private PersonService CreateService() => new(
        Db,
        teamRegions,
        new RegionLabels(NullLogger<RegionLabels>.Instance),
        audit);

    private Person AddPerson(string username)
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        };

        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    [Fact]
    public async Task ImportAsync_CreatesAPersonPerRow()
    {
        var result = await CreateService().ImportAsync(
            """
            username,firstName,lastName,email,homeCity
            jdoe,Jane,Doe,jane@example.test,ES-MD
            asmith,Alan,Smith,alan@example.test,CH-ZH
            """, Actor);

        result.Created.ShouldBe(["jdoe", "asmith"]);
        result.Failed.ShouldBeEmpty();

        var jane = Db.People.Single(p => p.Username == "jdoe");
        jane.FirstName.ShouldBe("Jane");
        jane.HomeCity.ShouldBe("ES-MD");
        jane.CreatedBy.ShouldBe(Actor);
    }

    [Fact]
    public async Task ImportAsync_SkipsUsernamesThatAlreadyExist()
    {
        AddPerson("jdoe");

        var result = await CreateService().ImportAsync("username\njdoe\nasmith", Actor);

        result.Created.ShouldBe(["asmith"]);
        var skipped = result.Skipped.ShouldHaveSingleItem();
        skipped.Username.ShouldBe("jdoe");
        skipped.Row.ShouldBe(2);
        Db.People.Count(p => p.Username == "jdoe").ShouldBe(1);
    }

    [Fact]
    public async Task ImportAsync_SkipsARepeatedRowWithinTheSameFile()
    {
        var result = await CreateService().ImportAsync("username\njdoe\njdoe", Actor);

        result.Created.ShouldBe(["jdoe"]);
        result.Skipped.ShouldHaveSingleItem().Row.ShouldBe(3);
    }

    [Fact]
    public async Task ImportAsync_ImportsTheValidRowsAndReportsTheRestByLineNumber()
    {
        var result = await CreateService().ImportAsync(
            """
            username,homeCity
            jdoe,ES-MD
            broken,not-a-region
            asmith,CH-ZH
            """, Actor);

        result.Created.ShouldBe(["jdoe", "asmith"]);
        var issue = result.Failed.ShouldHaveSingleItem();
        issue.Row.ShouldBe(3);
        issue.Username.ShouldBe("broken");
    }

    [Fact]
    public async Task ImportAsync_RejectsAValueTheApiWouldReject()
    {
        var result = await CreateService().ImportAsync(
            $"username,firstName\njdoe,{new string('x', 101)}", Actor);

        result.Created.ShouldBeEmpty();
        result.Failed.ShouldHaveSingleItem().Username.ShouldBe("jdoe");
    }

    [Fact]
    public async Task ImportAsync_FallsBackToTheNameThenTheUsernameForDisplayName()
    {
        await CreateService().ImportAsync(
            """
            username,firstName,lastName,displayName
            jdoe,Jane,Doe,
            asmith,,,
            """, Actor);

        Db.People.Single(p => p.Username == "jdoe").DisplayName.ShouldBe("Jane Doe");
        Db.People.Single(p => p.Username == "asmith").DisplayName.ShouldBe("asmith");
    }

    [Fact]
    public async Task ImportAsync_AcceptsHeadersInAnyOrderCaseOrSpelling()
    {
        await CreateService().ImportAsync("\"Home City\",USERNAME,First_Name\nES-MD,jdoe,Jane", Actor);

        var person = Db.People.Single();
        person.Username.ShouldBe("jdoe");
        person.FirstName.ShouldBe("Jane");
        person.HomeCity.ShouldBe("ES-MD");
    }

    [Fact]
    public async Task ImportAsync_ReportsARowWithNoUsername()
    {
        var result = await CreateService().ImportAsync("username,firstName\n,Jane", Actor);

        result.Failed.ShouldHaveSingleItem().Reason.ShouldBe("No username.");
    }

    [Fact]
    public async Task ImportAsync_WhenThereIsNoUsernameColumn_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => CreateService().ImportAsync("name,email\nJane,jane@example.test", Actor));
    }

    [Fact]
    public async Task ImportAsync_WhenTheFileIsEmpty_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(() => CreateService().ImportAsync("", Actor));
    }
}
