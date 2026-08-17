namespace Api.Configuration;

/// <summary>The team's own locality. Everything else about which regions the calendar
/// tracks is derived from where people say they are; this is the one region that is not,
/// so a brand new deployment with nobody in it still shows a useful calendar.</summary>
public sealed class TeamOptions
{
    public const string SectionName = "Team";

    /// <summary>ISO 3166-2, e.g. "CH-ZH". A bare country code ("CH") is valid too and
    /// pins only the nationwide holidays. Its holidays are synced whether or not anyone
    /// has picked it, and are never removed when the last person in it leaves.</summary>
    public string HomeRegion { get; init; } = "";

    /// <summary>An IANA timezone id, e.g. "Europe/Zurich". Required: every time-based
    /// absence's start/end time is wall-clock time in this one zone, for every viewer,
    /// with no per-viewer conversion, so there must always be exactly one to attach that
    /// meaning to.</summary>
    public string HomeTimeZone { get; init; } = "";
}
