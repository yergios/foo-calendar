using Api.Configuration;
using Api.Data.Models;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class PersonImportTests : SqliteTestBase
{
    private const string Actor = "admin";

    private readonly PersonColorPaletteOptions palette = ["#111111", "#222222", "#333333"];

    private readonly RecordingTeamRegionService teamRegions = new();

    private PersonService CreateService() => new(
        Db,
        Options.Create(palette),
        teamRegions,
        new RegionLabels(Options.Create<RegionCatalogOptions>([]), NullLogger<RegionLabels>.Instance));

    private Person AddPerson(string username, string color)
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            Color = color,
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
    public async Task ImportAsync_AssignsAFreeColorWhenNoneIsGiven()
    {
        AddPerson("existing", "#111111");

        await CreateService().ImportAsync("username\njdoe\nasmith", Actor);

        Db.People.Single(p => p.Username == "jdoe").Color.ShouldBe("#222222");
        Db.People.Single(p => p.Username == "asmith").Color.ShouldBe("#333333");
    }

    [Fact]
    public async Task ImportAsync_WhenThePaletteRunsOut_ReportsIt()
    {
        var result = await CreateService().ImportAsync("username\na\nb\nc\nd", Actor);

        result.Created.Count.ShouldBe(3);
        var issue = result.Failed.ShouldHaveSingleItem();
        issue.Username.ShouldBe("d");
        issue.Reason.ShouldContain("PersonColorPalette");
    }

    [Fact]
    public async Task ImportAsync_SkipsUsernamesThatAlreadyExist()
    {
        AddPerson("jdoe", "#111111");

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
    public async Task ImportAsync_RejectsAColorAlreadyTaken()
    {
        AddPerson("existing", "#111111");

        var result = await CreateService().ImportAsync("username,color\njdoe,#111111", Actor);

        result.Failed.ShouldHaveSingleItem().Reason.ShouldContain("already taken");
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
