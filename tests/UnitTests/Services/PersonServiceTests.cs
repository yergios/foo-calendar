using Api.Data.Models;
using Api.Dtos;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class PersonServiceTests : SqliteTestBase
{
    private readonly PersonService sut;
    private readonly RecordingTeamRegionService teamRegions = new();
    private readonly RecordingAuditLog audit = new();

    public PersonServiceTests()
    {
        sut = new PersonService(
            Db,
            teamRegions,
            new RegionLabels(NullLogger<RegionLabels>.Instance),
            audit);
    }

    private static CreatePersonRequest CreateRequest(string username) =>
        new(username, "Jane", "Doe", "Jane D.", null);

    [Fact]
    public async Task CreateAsync_WhenUsernameAlreadyExists_Throws()
    {
        await sut.CreateAsync(CreateRequest("jdoe"), "jdoe");

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.CreateAsync(CreateRequest("jdoe"), "jdoe"));
    }

    [Fact]
    public async Task UpdateAsync_WhenPersonDoesNotExist_ThrowsKeyNotFoundException()
    {
        await Should.ThrowAsync<KeyNotFoundException>(
            () => sut.UpdateAsync(999, new UpdatePersonRequest("Ghost", null), "admin"));
    }

    [Fact]
    public async Task DeleteAsync_WhenPersonDoesNotExist_ThrowsKeyNotFoundException()
    {
        await Should.ThrowAsync<KeyNotFoundException>(() => sut.DeleteAsync(999));
    }

    [Fact]
    public async Task DeleteAsync_RemovesThePerson()
    {
        var created = await sut.CreateAsync(CreateRequest("alice"), "alice");

        await sut.DeleteAsync(created.Id);

        var remaining = await sut.GetAllAsync();
        remaining.People.ShouldBeEmpty();
    }

    private static CreatePersonRequest CreateRequestIn(string username, string region) =>
        new(username, "Jane", "Doe", "Jane D.", region);

    [Fact]
    public async Task CreateAsync_SyncsTheRegionTheyJoined()
    {
        await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");

        teamRegions.Synced.ShouldBe(["ES-MD"]);
        teamRegions.Pruned.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenTheRegionChanges_SyncsTheNewOneAndPrunesTheOld()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");
        teamRegions.Synced.Clear();

        await sut.UpdateAsync(person.Id, new UpdatePersonRequest("Jane D.", "ES-AS"), "jdoe");

        teamRegions.Synced.ShouldBe(["ES-AS"]);
        teamRegions.Pruned.ShouldBe(["ES-MD"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheRegionIsUnchanged_LeavesHolidaysAlone()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");
        teamRegions.Synced.Clear();

        await sut.UpdateAsync(person.Id, new UpdatePersonRequest("Renamed", "ES-MD"), "jdoe");

        teamRegions.Synced.ShouldBeEmpty();
        teamRegions.Pruned.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_PrunesTheRegionTheyLeft()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");

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

    [Fact]
    public async Task DeleteAsync_LogsWhoTheyWereAndHowMuchWentWithThem()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");
        Db.Absences.Add(new Absence
        {
            PersonId = person.Id,
            StartDate = new DateOnly(2026, 3, 2),
            EndDate = new DateOnly(2026, 3, 6),
            Type = AbsenceType.Vacation,
            CreatedBy = "jdoe",
        });
        await Db.SaveChangesAsync();
        audit.Entries.Clear();

        await sut.DeleteAsync(person.Id);

        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditActions.PersonDeleted);
        entry.Details.ShouldBe("jdoe, ES-MD; 1 absence(s) deleted with them");
    }

    [Fact]
    public async Task UpdateAsync_LogsWhatMoved()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");
        audit.Entries.Clear();

        await sut.UpdateAsync(person.Id, new UpdatePersonRequest("Jane R.", "ES-MD"), "jdoe");

        var entry = audit.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditActions.PersonUpdated);
        entry.Details.ShouldBe("name Jane D. → Jane R.");
    }

    // A save that changed nothing is still a save. Logging it would bury the ones that did.
    [Fact]
    public async Task UpdateAsync_WhenNothingChanged_LogsNothing()
    {
        var person = await sut.CreateAsync(CreateRequestIn("jdoe", "ES-MD"), "jdoe");
        audit.Entries.Clear();

        await sut.UpdateAsync(person.Id, new UpdatePersonRequest("Jane D.", "ES-MD"), "jdoe");

        audit.Entries.ShouldBeEmpty();
    }
}
