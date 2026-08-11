namespace Api.Configuration;

public sealed class NagerOptions
{
    public const string SectionName = "Nager";

    public required string BaseUrl { get; init; }
}
