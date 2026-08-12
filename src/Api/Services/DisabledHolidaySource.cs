using Api.Data.Models;
using Api.Interfaces;

namespace Api.Services;

/// <summary>Stands in when Holidays:Source=None, so the rest of the app keeps its single
/// dependency on IHolidaySource instead of everything having to handle its absence. The
/// background sync isn't registered in that mode and the admin endpoint refuses up front,
/// so this exists to make "nothing to sync" the honest answer if it is ever reached.
/// </summary>
public sealed class DisabledHolidaySource : IHolidaySource
{
    // Never actually recorded: this source produces no rows to stamp.
    public HolidaySource Kind => HolidaySource.Manual;

    public Task<IReadOnlyList<SourceHoliday>> GetAsync(
        string countryCode, int year, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SourceHoliday>>([]);
}
