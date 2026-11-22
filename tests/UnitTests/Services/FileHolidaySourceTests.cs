using Api.Configuration;
using Api.Data.Models;
using Api.Services;

using Microsoft.Extensions.Options;

using Shouldly;

namespace UnitTests.Services;

public sealed class FileHolidaySourceTests : IDisposable
{
    private readonly List<string> writtenFiles = [];

    private FileHolidaySource CreateSource(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"holidays-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        writtenFiles.Add(path);

        return new FileHolidaySource(Options.Create(new HolidayOptions
        {
            Source = HolidayOptions.SourceFile,
            File = new HolidayFileOptions { Path = path },
        }));
    }

    public void Dispose()
    {
        foreach (var path in writtenFiles) File.Delete(path);
    }

    [Fact]
    [Trait("Spec", "HOL-5")]
    public async Task GetAsync_ReturnsEntriesForTheCountryAndItsSubdivisions()
    {
        var sut = CreateSource("""
            [
              { "region": "CH", "date": "2026-08-01", "name": "National Day" },
              { "region": "CH-ZH", "date": "2026-04-13", "name": "Sechselauten" },
              { "region": "ES-MD", "date": "2026-05-02", "name": "Fiesta de la Comunidad" }
            ]
            """);

        var holidays = await sut.GetAsync("CH", 2026);

        holidays.Select(h => h.Region).ShouldBe(["CH", "CH-ZH"], ignoreOrder: true);
    }

    [Fact]
    [Trait("Spec", "HOL-5")]
    public async Task GetAsync_IgnoresEntriesForOtherYears()
    {
        var sut = CreateSource("""
            [
              { "region": "CH", "date": "2026-08-01", "name": "This year" },
              { "region": "CH", "date": "2027-08-01", "name": "Next year" }
            ]
            """);

        var holidays = await sut.GetAsync("CH", 2026);

        holidays.ShouldHaveSingleItem().Name.ShouldBe("This year");
    }

    [Fact]
    [Trait("Spec", "HOL-5")]
    public async Task GetAsync_ProjectsARecurringEntryOntoTheRequestedYear()
    {
        var sut = CreateSource("""
            [{ "region": "CH", "date": "2020-12-24", "name": "Office closed", "recurringAnnually": true }]
            """);

        var holidays = await sut.GetAsync("CH", 2026);

        holidays.ShouldHaveSingleItem().Date.ShouldBe(new DateOnly(2026, 12, 24));
    }

    [Fact]
    [Trait("Spec", "HOL-5")]
    public async Task GetAsync_SkipsARecurringLeapDayOnANonLeapYear()
    {
        var sut = CreateSource("""
            [{ "region": "CH", "date": "2020-02-29", "name": "Leap day", "recurringAnnually": true }]
            """);

        var holidays = await sut.GetAsync("CH", 2026);

        holidays.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "HOL-5")]
    public async Task GetAsync_WhenTheFileIsMissing_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"holidays-{Guid.NewGuid():N}.json");
        var sut = new FileHolidaySource(Options.Create(new HolidayOptions
        {
            Source = HolidayOptions.SourceFile,
            File = new HolidayFileOptions { Path = missing },
        }));

        await Should.ThrowAsync<FileNotFoundException>(() => sut.GetAsync("CH", 2026));
    }

    [Fact]
    public async Task Kind_IsFile()
    {
        var sut = CreateSource("[]");

        sut.Kind.ShouldBe(HolidaySource.File);
        (await sut.GetAsync("CH", 2026)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "REG-5")]
    public async Task GetCountriesAsync_DerivesCountriesFromWhateverTheFileMentions()
    {
        var sut = CreateSource("""
            [
              { "region": "CH", "date": "2026-08-01", "name": "National Day" },
              { "region": "CH-ZH", "date": "2026-04-13", "name": "Sechselauten" },
              { "region": "ES-MD", "date": "2026-05-02", "name": "Dos de Mayo" }
            ]
            """);

        var countries = await sut.GetCountriesAsync();

        countries.ShouldBe(["CH", "ES"]);
    }

    [Fact]
    [Trait("Spec", "REG-5")]
    public async Task GetRegionsAsync_ReturnsSubdivisionsOfOneCountryWithoutTheCountryItself()
    {
        var sut = CreateSource("""
            [
              { "region": "CH", "date": "2026-08-01", "name": "National Day" },
              { "region": "CH-ZH", "date": "2026-04-13", "name": "Sechselauten" },
              { "region": "CH-BE", "date": "2026-01-02", "name": "Berchtoldstag" },
              { "region": "ES-MD", "date": "2026-05-02", "name": "Dos de Mayo" }
            ]
            """);

        var regions = await sut.GetRegionsAsync("CH");

        regions.ShouldBe(["CH-BE", "CH-ZH"]);
    }
}
