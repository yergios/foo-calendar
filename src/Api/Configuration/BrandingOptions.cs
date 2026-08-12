namespace Api.Configuration;

/// <summary>Per-deployment identity of the app. Served to the SPA at runtime (see
/// ConfigEndpoints) so a team can rebrand by editing config, without rebuilding the
/// frontend bundle.</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string AppName { get; init; } = "Foo Calendar";

    /// <summary>Where "Contact support" points. Any URL scheme, commonly mailto:.</summary>
    public string SupportUrl { get; init; } = "";

    /// <summary>Where "Raise an issue" points, e.g. an issue tracker or a helpdesk form.</summary>
    public string IssueUrl { get; init; } = "";
}
