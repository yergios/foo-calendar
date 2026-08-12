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

    public async Task<IReadOnlyList<SourceHoliday>> GetAsync(
        string countryCode, int year, CancellationToken ct = default)
    {
        // Read on every sync rather than cached at startup: the file is small, syncs are
        // rare, and an edited file should take effect on the next sync rather than
        // requiring a restart. A missing or malformed file throws, which the caller
        // reports as a failed country instead of silently syncing nothing.
        await using var stream = File.OpenRead(options.Value.File.Path);
        var entries = await JsonSerializer
            .DeserializeAsync<List<FileHoliday>>(stream, JsonOptions, ct) ?? [];

        return
        [
            .. entries
                .Where(e => RegionCode.BelongsTo(e.Region, countryCode))
                .Select(e => (Entry: e, Date: e.RecurringAnnually
                    ? BankHoliday.OccurrenceIn(e.Date, year)
                    : e.Date))
                .Where(x => x.Date is DateOnly d && d.Year == year)
                .Select(x => new SourceHoliday(x.Entry.Region, x.Date!.Value, x.Entry.Name))
        ];
    }
}
