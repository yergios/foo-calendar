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
}
