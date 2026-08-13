using Api.Data.Models;

namespace Api.Interfaces;

/// <summary>One holiday as it arrives from a source, already resolved to the region it
/// applies to. Whatever shape the source uses to express "nationwide" is its own business
/// and has been expanded by the time it gets here.</summary>
public sealed record SourceHoliday(string Region, DateOnly Date, string Name);

/// <summary>Where public holidays come from. Swapping the implementation is how a
/// deployment that can't reach the public internet, or that observes company-specific
/// closures, gets its calendar filled in.</summary>
public interface IHolidaySource
{
    /// <summary>Recorded on the rows this source produces, so it stays visible in the UI
    /// and in the audit columns which system put a holiday there.</summary>
    HolidaySource Kind { get; }

    /// <summary>Holidays for one country and year. Returning an empty list means "nothing
    /// to add", while throwing means the fetch failed and the caller should report it.
    /// </summary>
    Task<IReadOnlyList<SourceHoliday>> GetAsync(
        string countryCode, int year, CancellationToken ct = default);

    /// <summary>Every country this source can supply holidays for, as ISO 3166-1 codes.
    /// Backs the region picker on the profile page, so it answers "what could someone
    /// pick", not "what does this team track". Empty is a valid answer and leaves the
    /// picker with only the regions already in use.</summary>
    Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default);

    /// <summary>The subdivisions of one country this source distinguishes, as ISO 3166-2
    /// codes. Empty means the source only has nationwide holidays for it, which is the
    /// common case: most countries have no regional holidays at all.</summary>
    Task<IReadOnlyList<string>> GetRegionsAsync(
        string countryCode, CancellationToken ct = default);
}
