using System.Text.Json;

using Api.Configuration;
using Api.Data.Models;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>Public holidays from a JSON file, typically mounted into the container. For
/// networks with no outbound internet access, and for company closures that no public
/// holiday API knows about.</summary>
public sealed class FileHolidaySource(IOptions<HolidayOptions> options) : IHolidaySource
{
    /// <summary>One entry in the file. With RecurringAnnually the year in Date is ignored
    /// and its month/day is projected onto whichever year is being synced.</summary>
    private sealed record FileHoliday(string Region, DateOnly Date, string Name, bool RecurringAnnually);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HolidaySource Kind => HolidaySource.File;

    // Read on every call rather than cached at startup: the file is small, calls are rare,
    // and an edited file should take effect on the next sync rather than requiring a
    // restart. A missing or malformed file throws, which the caller reports as a failed
    // country instead of silently syncing nothing.
    private async Task<List<FileHoliday>> ReadAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(options.Value.File.Path);
        return await JsonSerializer.DeserializeAsync<List<FileHoliday>>(stream, JsonOptions, ct) ?? [];
    }

    public async Task<IReadOnlyList<SourceHoliday>> GetAsync(
        string countryCode, int year, CancellationToken ct = default)
    {
        var entries = await ReadAsync(ct);

        return
        [
            .. entries
                .Where(e => RegionCode.BelongsTo(e.Region, countryCode))
                .Select(e => (Entry: e, Date: e.RecurringAnnually
                    ? BankHoliday.OccurrenceIn(e.Date, year)
                    : e.Date))
                .Where(x => x.Date is DateOnly d && d.Year == year)
                .Select(x => new SourceHoliday(x.Entry.Region, x.Date.Value, x.Entry.Name))
        ];
    }

    // Whatever the file mentions is exactly what this source can offer, so both lists are
    // derived from its contents rather than from any external catalogue.
    public async Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default) =>
    [
        .. (await ReadAsync(ct))
            .Select(e => RegionCode.CountryOf(e.Region))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];

    public async Task<IReadOnlyList<string>> GetRegionsAsync(
        string countryCode, CancellationToken ct = default) =>
    [
        .. (await ReadAsync(ct))
            .Select(e => e.Region)
            .Where(r => r != countryCode && RegionCode.BelongsTo(r, countryCode))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];
}
