namespace Api.Configuration;

/// <summary>Who this deployment belongs to and where it sits. The team's own locality
/// drives which holidays are synced regardless of who is in the app; the rest is how the
/// deployment names and supports itself. None of it is the product's identity, which is
/// fixed in <see cref="ProductInfo"/>.</summary>
public sealed class TeamOptions
{
    public const string SectionName = "Team";

    /// <summary>The deploying team's own name, shown in the header, e.g. "Acme
    /// Engineering". Empty falls back to the product's name, so the header always has
    /// something to link home with. This is the header; the footer's attribution is the
    /// product's own and is not configurable (see ProductInfo and TRADEMARK.md).</summary>
    public string OrgName { get; init; } = "";

    /// <summary>ISO 3166-2, e.g. "CH-ZH". A bare country code ("CH") is valid too and
    /// pins only the nationwide holidays. Its holidays are synced whether or not anyone
    /// has picked it, and are never removed when the last person in it leaves.</summary>
    public string HomeRegion { get; init; } = "";

    /// <summary>An IANA timezone id, e.g. "Europe/Zurich". Required: every time-based
    /// absence's start/end time is wall-clock time in this one zone, for every viewer,
    /// with no per-viewer conversion, so there must always be exactly one to attach that
    /// meaning to.</summary>
    public string HomeTimeZone { get; init; } = "";

    /// <summary>Where "Contact support" points. Any URL scheme, commonly mailto:. Stays
    /// runtime config rather than a compile-time constant: it is deployment-specific and
    /// carries no styling, so changing a helpdesk address shouldn't need a rebuild.</summary>
    public string SupportUrl { get; init; } = "";

    /// <summary>Where "Raise an issue" points, e.g. an issue tracker or a helpdesk form.</summary>
    public string IssueUrl { get; init; } = "";
}
