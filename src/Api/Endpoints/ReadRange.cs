using Api.Services;

namespace Api.Endpoints;

/// <summary>The bound on a from..to read. The SPA never asks for more than a year, and
/// without a bound one request could make the server read and serialize every absence or
/// holiday ever stored.</summary>
public static class ReadRange
{
    /// <summary>A leap year, both ends included.</summary>
    public const int MaxDays = 366;

    /// <summary>Throws InvalidInputException, which answers 400 with a problem detail, when
    /// <paramref name="to"/> is before <paramref name="from"/> or the range spans more than
    /// <see cref="MaxDays"/> days.</summary>
    public static void EnsureValid(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new InvalidInputException($"'to' ({to:yyyy-MM-dd}) is before 'from' ({from:yyyy-MM-dd}).");

        var days = to.DayNumber - from.DayNumber + 1;
        if (days > MaxDays)
            throw new InvalidInputException(
                $"A range may span at most {MaxDays} days; {from:yyyy-MM-dd} to {to:yyyy-MM-dd} spans {days}.");
    }
}
