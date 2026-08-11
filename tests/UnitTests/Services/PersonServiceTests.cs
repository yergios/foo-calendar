using Api.Dtos;
using Api.Services;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class PersonServiceTests : SqliteTestBase
{
    private readonly PersonService sut;

    public PersonServiceTests()
    {
        sut = new PersonService(Db);
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
}
