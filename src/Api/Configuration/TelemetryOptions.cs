namespace Api.Configuration;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public const string EnvironmentVariableName = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public string ApplicationInsightsConnectionString { get; init; } = string.Empty;
}
