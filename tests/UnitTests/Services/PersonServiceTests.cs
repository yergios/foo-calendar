using Api.Configuration;
using Api.Dtos;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class PersonServiceTests : SqliteTestBase
{
    private readonly PersonService sut;
    private readonly RecordingTeamRegionService teamRegions = new();

    public PersonServiceTests()
    {
        sut = new PersonService(
            Db,
            Options.Create<PersonColorPaletteOptions>([]),
            teamRegions,
            new RegionLabels(Options.Create<RegionCatalogOptions>([]), NullLogger<RegionLabels>.Instance));
    }

    private static CreatePersonRequest CreateRequest(string username, string color) =>
        new(username, "Jane", "Doe", "Jane D.", null, color);

    [Fact]
    public async Task CreateAsync_WhenUsernameAlreadyExists_Throws()
    {
        await sut.CreateAsync(CreateRequest("jdoe", "#111111"), "jdoe");

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(CreateRequest("jdoe", "#222222"), "jdoe"));
    }

    [Fact]
    public async Task CreateAsync_WhenColorAlreadyTaken_Throws()
    {
        await sut.CreateAsync(CreateRequest("alice", "#111111"), "alice");

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(CreateRequest("bob", "#111111"), "bob"));
    }

    [Fact]
    public async Task CreateAsync_WhenColorDiffersOnlyInCase_Throws()
    {
        await sut.CreateAsync(CreateRequest("alice", "#a1b2c3"), "alice");

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(CreateRequest("bob", "#A1B2C3"), "bob"));
    }

    [Fact]
    public async Task CreateAsync_StoresACustomColorLowerCased()
    {
        var created = await sut.CreateAsync(CreateRequest("alice", "#A1B2C3"), "alice");

        created.Color.ShouldBe("#a1b2c3");
    }

    [Fact]
    public async Task UpdateAsync_WhenTakingSomeoneElsesColorInAnotherCase_Throws()
    {
        var alice = await sut.CreateAsync(CreateRequest("alice", "#111111"), "alice");
        await sut.CreateAsync(CreateRequest("bob", "#a1b2c3"), "bob");

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateAsync(alice.Id, new UpdatePersonRequest("Alice", null, "#A1B2C3"), "alice"));
    }

    [Fact]
    public async Task UpdateAsync_WhenKeepingYourOwnColor_Succeeds()
    {
        var created = await sut.CreateAsync(CreateRequest("alice", "#111111"), "alice");

        var updated = await sut.UpdateAsync(created.Id, new UpdatePersonRequest("Alice", null, "#111111"), "alice");

        updated.Color.ShouldBe("#111111");
    }

    [Fact]
    public async Task UpdateAsync_WhenTakingSomeoneElsesColor_Throws()
    {
        var alice = await sut.CreateAsync(CreateRequest("alice", "#111111"), "alice");
        await sut.CreateAsync(CreateRequest("bob", "#222222"), "bob");

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateAsync(alice.Id, new UpdatePersonRequest("Alice", null, "#222222"), "alice"));
    }

    [Fact]
    public async Task UpdateAsync_WhenPersonDoesNotExist_ThrowsKeyNotFoundException()
    {
        await Should.ThrowAsync<KeyNotFoundException>(
            () => sut.UpdateAsync(999, new UpdatePersonRequest("Ghost", null, "#333333"), "admin"));
    }

    [Fact]
    public async Task DeleteAsync_WhenPersonDoesNotExist_ThrowsKeyNotFoundException()
    {
        await Should.ThrowAsync<KeyNotFoundException>(() => sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAsync_RemovesThePerson()
    {
        var created = await sut.CreateAsync(CreateRequest("alice", "#111111"), "alice");

        await sut.DeleteAsync(created.Id);

        var remaining = await sut.GetAllAsync();
        remaining.People.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetColorsInUseAsync_ReturnsEveryPersonsColor()
    {
        await sut.CreateAsync(CreateRequest("alice", "#111111"), "alice");
        await sut.CreateAsync(CreateRequest("bob", "#222222"), "bob");

        var colors = await sut.GetColorsInUseAsync();

        colors.Colors.ShouldBe(["#111111", "#222222"], ignoreOrder: true);
    }

    private static CreatePersonRequest CreateRequestIn(string username, string color, string region) =>
        new(username, "Jane", "Doe", "Jane D.", region, color);

    [Fact]
    public async Task CreateAsync_SyncsTheRegionTheyJoined()
    {
        await sut.CreateAsync(CreateRequestIn("jdoe", "#111111", "ES-MD"), "jdoe");

        teamRegions.Synced.ShouldBe(["ES-MD"]);
        teamRegions.Pruned.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenTheRegionChanges_SyncsTheNewOneAndPrunesTheOld()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "#111111", "ES-MD"), "jdoe");
        teamRegions.Synced.Clear();

        await sut.UpdateAsync(person.Id, new UpdatePersonRequest("Jane D.", "ES-AS", "#111111"), "jdoe");

        teamRegions.Synced.ShouldBe(["ES-AS"]);
        teamRegions.Pruned.ShouldBe(["ES-MD"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheRegionIsUnchanged_LeavesHolidaysAlone()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "#111111", "ES-MD"), "jdoe");
        teamRegions.Synced.Clear();

        await sut.UpdateAsync(person.Id, new UpdatePersonRequest("Renamed", "ES-MD", "#111111"), "jdoe");

        teamRegions.Synced.ShouldBeEmpty();
        teamRegions.Pruned.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_PrunesTheRegionTheyLeft()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "#111111", "ES-MD"), "jdoe");

        await sut.DeleteAsync(person.Id);

        teamRegions.Pruned.ShouldBe(["ES-MD"]);
    }

    [Fact]
    public async Task GetAllAsync_LabelsEveryRegionInUseAndItsCountry()
    {
        teamRegions.InUse = ["ES-MD"];

        var result = await sut.GetAllAsync();

        // No name table on disk in the test output, so labels fall back to the raw codes.
        // What matters here is that the country turns up alongside the region.
        result.RegionLabels.Keys.ShouldBe(["ES", "ES-MD"], ignoreOrder: true);
    }
}
