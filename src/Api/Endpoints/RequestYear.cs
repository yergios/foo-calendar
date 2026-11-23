using Api.Services;

namespace Api.Endpoints;

/// <summary>The bound on a year a request names, for the stats and the holiday sync. A
/// year DateOnly cannot hold would otherwise fail deep in the date arithmetic as a 500, and
/// so would 9999: counting a person's working days steps one day past the end of the year,
/// which DateOnly cannot represent. The bound is far inside both, and still wide enough for
/// any year a team calendar has a reason to ask about.</summary>
public static class RequestYear
{
    public const int Min = 1900;
    public const int Max = 2999;

    /// <summary>Throws InvalidInputException, which answers 400 with a problem detail, when
    /// <paramref name="year"/> is outside <see cref="Min"/>..<see cref="Max"/>.</summary>
    public static void EnsureValid(int year)
    {
        if (year is < Min or > Max)
            throw new InvalidInputException($"'year' must be between {Min} and {Max}; got {year}.");
    }
}
