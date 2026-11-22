namespace Api.Configuration;

/// <summary>Where public holidays come from, and how often they are refreshed.</summary>
public sealed class HolidayOptions
{
    public const string SectionName = "Holidays";

    /// <summary>Pulls from the Nager.Date web API. The default, and it needs outbound
    /// internet access.</summary>
    public const string SourceNager = "Nager";

    /// <summary>Reads a JSON file, for networks with no egress, or for company closures
    /// that no public holiday API knows about.</summary>
    public const string SourceFile = "File";

    /// <summary>No automatic holidays at all. Whatever admins add by hand is all there is.
    /// </summary>
    public const string SourceNone = "None";

    public string Source { get; init; } = SourceNager;

    /// <summary>How often the background job re-syncs. Public holiday data barely changes
    /// once published, so the default is deliberately slow; a file source that changes more
    /// often is the reason this is configurable at all.</summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromDays(7);

    public NagerOptions Nager { get; init; } = new();
    public HolidayFileOptions File { get; init; } = new();
}

public sealed class NagerOptions
{
    public string BaseUrl { get; init; } = "https://date.nager.at/api/v3/";
}

public sealed class HolidayFileOptions
{
    /// <summary>Path to the JSON file, typically mounted into the container. See "The file
    /// source" in docs/regions-and-holidays.md for the format.</summary>
    public string Path { get; init; } = "";
}
