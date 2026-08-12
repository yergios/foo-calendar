namespace Api.Configuration;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public const string EndpointEnvironmentVariableName = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string ProtocolEnvironmentVariableName = "OTEL_EXPORTER_OTLP_PROTOCOL";
    public const string ServiceNameEnvironmentVariableName = "OTEL_SERVICE_NAME";

    /// <summary>Accepted <see cref="OtlpProtocol"/> values, as spelled by the OTLP spec.</summary>
    public const string ProtocolGrpc = "grpc";
    public const string ProtocolHttpProtobuf = "http/protobuf";

    /// <summary>
    /// Where to push traces, metrics, and logs over OTLP. Empty falls back to the
    /// OTEL_EXPORTER_OTLP_ENDPOINT environment variable, which the SDK reads itself.
    /// With neither set, export is switched off rather than retrying localhost:4317.
    /// </summary>
    public string OtlpEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// "grpc" (port 4317) or "http/protobuf" (port 4318). Empty falls back to the
    /// OTEL_EXPORTER_OTLP_PROTOCOL environment variable, then to grpc.
    /// </summary>
    public string OtlpProtocol { get; init; } = string.Empty;

    /// <summary>
    /// Reported as service.name. Empty falls back to OTEL_SERVICE_NAME, then to the
    /// application name.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;
}
