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

    // A real AbsenceService, not a fake: sorting by a stats column (see
    // GetPageAsync_SortedByVacation_OrdersAcrossPagesByThatYearsStats below) exercises its
    // actual day-counting, which is the whole point of that path existing, and this is
    // also what the tests for it use to give a person some vacation days.
    private readonly AbsenceService absences;

    public PersonServiceTests()
    {
        absences = TestAbsenceService.Create(Db);
        sut = new PersonService(
            Db,
            teamRegions,
            new RegionLabels(NullLogger<RegionLabels>.Instance),
            audit,
            absences);
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

    private static CreatePersonRequest CreateRequestNamed(string username, string displayName, string region = null) =>
        new(username, "First", "Last", displayName, region);

    [Fact]
    public async Task GetPageAsync_FiltersBySearch_MatchingDisplayNameSubstringCaseInsensitively()
    {
        await sut.CreateAsync(CreateRequestNamed("asmith", "Alice Smith"), "admin");
        await sut.CreateAsync(CreateRequestNamed("bjones", "Bob Jones"), "admin");

        var result = await sut.GetPageAsync(1, 25, "smith", null, PersonSortColumn.Name, false, null);

        result.Total.ShouldBe(1);
        result.People.ShouldHaveSingleItem().DisplayName.ShouldBe("Alice Smith");
    }

    [Fact]
    public async Task GetPageAsync_SearchTreatsUnderscoreLiterally_NotAsAOneCharacterWildcard()
    {
        await sut.CreateAsync(CreateRequestNamed("jdoe1", "j_doe"), "admin");
        await sut.CreateAsync(CreateRequestNamed("jdoe2", "jxdoe"), "admin");

        var result = await sut.GetPageAsync(1, 25, "j_doe", null, PersonSortColumn.Name, false, null);

        result.Total.ShouldBe(1);
        result.People.ShouldHaveSingleItem().DisplayName.ShouldBe("j_doe");
    }

    [Fact]
    public async Task GetPageAsync_WhenDisplayNamesTie_PagesByIdWithNoDuplicatesOrGaps()
    {
        await sut.CreateAsync(CreateRequestNamed("a", "Same Name"), "admin");
        await sut.CreateAsync(CreateRequestNamed("b", "Same Name"), "admin");
        await sut.CreateAsync(CreateRequestNamed("c", "Same Name"), "admin");

        var firstPage = await sut.GetPageAsync(1, 2, null, null, PersonSortColumn.Name, false, null);
        var secondPage = await sut.GetPageAsync(2, 2, null, null, PersonSortColumn.Name, false, null);

        firstPage.People.Select(p => p.Username).ShouldBe(["a", "b"]);
        secondPage.People.Select(p => p.Username).ShouldBe(["c"]);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByRegion_MatchingItAndItsSubdivisionsOnly()
    {
        await sut.CreateAsync(CreateRequestIn("madrid", "ES-MD"), "admin");
        await sut.CreateAsync(CreateRequestIn("asturias", "ES-AS"), "admin");
        await sut.CreateAsync(CreateRequestIn("california", "US-CA"), "admin");

        var result = await sut.GetPageAsync(1, 25, null, ["ES"], PersonSortColumn.Name, false, null);

        result.Total.ShouldBe(2);
        result.People.Select(p => p.Username).ShouldBe(["madrid", "asturias"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetPageAsync_SortsByUsernameDescending()
    {
        await sut.CreateAsync(CreateRequest("alice"), "admin");
        await sut.CreateAsync(CreateRequest("bob"), "admin");
        await sut.CreateAsync(CreateRequest("carl"), "admin");

        var result = await sut.GetPageAsync(1, 25, null, null, PersonSortColumn.Username, descending: true, null);

        result.People.Select(p => p.Username).ShouldBe(["carl", "bob", "alice"]);
    }

    // Vacation days aren't a database column, so sorting by that column has its own code
    // path (see PersonService.GetStatsSortedPageAsync) that has to page correctly too, not
    // just sort the first page it's handed - this pins that down with a page size that
    // splits the three people across two requests.
    [Fact]
    public async Task GetPageAsync_SortedByVacation_OrdersAcrossPagesByThatYearsStats()
    {
        await sut.CreateAsync(CreateRequestNamed("noVacation", "No Vacation"), "admin");
        var oneWeek = await sut.CreateAsync(CreateRequestNamed("oneWeek", "One Week"), "admin");
        var twoWeeks = await sut.CreateAsync(CreateRequestNamed("twoWeeks", "Two Weeks"), "admin");

        var person = await Db.People.FindAsync(oneWeek.Id);
        await absences.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), AbsenceType.Vacation, null));

        person = await Db.People.FindAsync(twoWeeks.Id);
        await absences.CreateAsync(
            person,
            new CreateAbsenceRequest(new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 17), AbsenceType.Vacation, null));

        var firstPage = await sut.GetPageAsync(1, 2, null, null, PersonSortColumn.Vacation, false, 2026);
        var secondPage = await sut.GetPageAsync(2, 2, null, null, PersonSortColumn.Vacation, false, 2026);

        firstPage.Total.ShouldBe(3);
        firstPage.People.Select(p => p.Username).ShouldBe(["noVacation", "oneWeek"]);
        secondPage.People.Select(p => p.Username).ShouldBe(["twoWeeks"]);
    }
}
