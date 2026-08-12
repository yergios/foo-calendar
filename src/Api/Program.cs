using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Api.Configuration;
using Api.Data;
using Api.Dtos;
using Api.Endpoints;
using Api.Infrastructure;
using Api.Interfaces;
using Api.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var services = builder.Services;

// ---- Options --------------------------------------------------------------

services.AddOptions<RegionCatalogOptions>()
    .Bind(config.GetSection(RegionCatalogOptions.SectionName))
    .Validate(regions => regions.Count > 0, "RegionCatalog must contain at least one country.")
    .ValidateOnStart();

services.AddOptions<PersonColorPaletteOptions>()
    .Bind(config.GetSection(PersonColorPaletteOptions.SectionName))
    .Validate(colors => colors.Count > 0, "PersonColorPalette must contain at least one color.")
    .Validate(colors => colors.All(c => Regex.IsMatch(c, ValidationPatterns.HexColor)),
        "PersonColorPalette entries must be valid 6-digit hex colors.")
    .ValidateOnStart();

services.AddOptions<HolidayOptions>()
    .Bind(config.GetSection(HolidayOptions.SectionName))
    .Validate(
        o => o.Source is HolidayOptions.SourceNager
            or HolidayOptions.SourceFile
            or HolidayOptions.SourceNone,
        "Holidays:Source must be one of Nager, File, None.")
    .Validate(
        o => o.Source != HolidayOptions.SourceNager || !string.IsNullOrWhiteSpace(o.Nager.BaseUrl),
        "Holidays:Nager:BaseUrl must be configured.")
    .Validate(
        o => o.Source != HolidayOptions.SourceFile || !string.IsNullOrWhiteSpace(o.File.Path),
        "Holidays:File:Path must be configured when Holidays:Source=File.")
    .Validate(o => o.SyncInterval > TimeSpan.Zero, "Holidays:SyncInterval must be positive.")
    .ValidateOnStart();

services.AddOptions<BrandingOptions>()
    .Bind(config.GetSection(BrandingOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.AppName), "Branding:AppName must be configured.")
    .ValidateOnStart();

services.AddOptions<WebhookOptions>()
    .Bind(config.GetSection(WebhookOptions.SectionName))
    .Validate(
        o => string.IsNullOrWhiteSpace(o.Url) || Uri.IsWellFormedUriString(o.Url, UriKind.Absolute),
        "Webhook:Url must be an absolute URL.")
    .Validate(o => o.MaxAttempts >= 1, "Webhook:MaxAttempts must be at least 1.")
    .Validate(o => o.Timeout > TimeSpan.Zero, "Webhook:Timeout must be positive.")
    .ValidateOnStart();

services.AddOptions<ReverseProxyOptions>()
    .Bind(config.GetSection(ReverseProxyOptions.SectionName))
    .Validate(o => o.PathBase.Length == 0 || o.PathBase.StartsWith('/'),
        "ReverseProxy:PathBase must start with '/', e.g. \"/calendar\".")
    .Validate(o => o.PathBase.Length == 0 || !o.PathBase.EndsWith('/'),
        "ReverseProxy:PathBase must not end with '/'.")
    .ValidateOnStart();

// ---- Database -------------------------------------------------------------

services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(
        config.GetConnectionString(ConnectionStrings.FooCalendarDb)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStrings.FooCalendarDb} must be configured.")));

// ---- Services -------------------------------------------------------------

services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

services.AddHttpContextAccessor();

// ---- Holidays -------------------------------------------------------------

var holidayConfig = config.GetSection(HolidayOptions.SectionName).Get<HolidayOptions>()
    ?? new HolidayOptions();

switch (holidayConfig.Source)
{
    case HolidayOptions.SourceFile:
        services.AddSingleton<IHolidaySource, FileHolidaySource>();
        break;

    case HolidayOptions.SourceNone:
        services.AddSingleton<IHolidaySource, DisabledHolidaySource>();
        break;

    // Anything else is rejected by the options validation above before the app serves.
    default:
        services.AddHttpClient<IHolidaySource, NagerHolidaySource>((sp, client) =>
            client.BaseAddress = new Uri(
                sp.GetRequiredService<IOptions<HolidayOptions>>().Value.Nager.BaseUrl));
        break;
}

// Nothing to poll for when there is no source; admins can still add holidays by hand.
if (holidayConfig.Source != HolidayOptions.SourceNone)
    services.AddHostedService<BankHolidaySyncBackgroundService>();

services.AddScoped<IBankHolidayService, BankHolidayService>();

// ---- Webhook --------------------------------------------------------------

// Always registered so callers have something to publish to; it drops events on the floor
// until a URL is configured. The delivery loop is only started when there is one.
services.AddHttpClient(WebhookPublisher.HttpClientName);
services.AddSingleton<WebhookPublisher>();
services.AddSingleton<IWebhookPublisher>(sp => sp.GetRequiredService<WebhookPublisher>());

if (!string.IsNullOrWhiteSpace(config[$"{WebhookOptions.SectionName}:{nameof(WebhookOptions.Url)}"]))
    services.AddHostedService(sp => sp.GetRequiredService<WebhookPublisher>());

services.AddScoped<IAbsenceService, AbsenceService>();
services.AddScoped<IPersonService, PersonService>();
services.AddScoped<ICurrentPersonAccessor, CurrentPersonAccessor>();

// ---- Validation & Error handling ------------------------------------------

services.AddValidation();
services.AddProblemDetails();
services.AddExceptionHandler<DomainExceptionHandler>();

// ---- Health checks --------------------------------------------------------

services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("sqlite",
        failureStatus: HealthStatus.Degraded);

// ---- Auth -----------------------------------------------------------------

var authConfig = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
    ?? throw new InvalidOperationException("Auth options are not configured.");

if (authConfig.Provider == AuthOptions.ProviderNone && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Auth:Provider=None is only allowed in Development.");

if (authConfig.Provider != AuthOptions.ProviderNone)
{
    if (string.IsNullOrWhiteSpace(authConfig.Authority))
        throw new InvalidOperationException("Auth:Authority must be configured.");
    if (string.IsNullOrWhiteSpace(authConfig.Audience))
        throw new InvalidOperationException("Auth:Audience must be configured.");
    // The API never uses this itself, but the SPA can't start a login without it, and
    // failing at startup beats a frontend that only breaks once someone tries to sign in.
    if (string.IsNullOrWhiteSpace(authConfig.ClientId))
        throw new InvalidOperationException("Auth:ClientId must be configured.");
}

services.AddOptions<AuthOptions>()
    .Bind(config.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();

services.AddAuthorization();

if (authConfig.Provider == AuthOptions.ProviderNone)
{
    services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });
}
else
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authConfig.Authority;
            options.Audience = authConfig.Audience;
        });
}

// ---- Reverse proxy --------------------------------------------------------

var proxyConfig = config.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
    ?? new ReverseProxyOptions();

if (proxyConfig.TrustForwardedHeaders)
{
    services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;

        // The defaults trust loopback only, which is never where the proxy sits in a
        // container deployment. Start from nothing and add back what was configured;
        // leaving both empty accepts forwarded headers from any peer.
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();

        foreach (var proxy in proxyConfig.KnownProxies)
            o.KnownProxies.Add(IPAddress.Parse(proxy));

        foreach (var network in proxyConfig.KnownNetworks)
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    });
}

// ---- Observability --------------------------------------------------------

services.AddOptions<TelemetryOptions>()
    .Bind(config.GetSection(TelemetryOptions.SectionName))
    .ValidateOnStart();

// The config value wins when set; otherwise fall back to the OTEL_* env vars that every
// OpenTelemetry-aware collector and platform already sets. Blank counts as unset, since
// appsettings.json ships the keys with empty defaults.
var telemetryOptions = config.GetSection(TelemetryOptions.SectionName)
    .Get<TelemetryOptions>() ?? new TelemetryOptions();

var otlpEndpoint = telemetryOptions.OtlpEndpoint;
if (string.IsNullOrWhiteSpace(otlpEndpoint))
    otlpEndpoint = config[TelemetryOptions.EndpointEnvironmentVariableName];

var serviceName = telemetryOptions.ServiceName;
if (string.IsNullOrWhiteSpace(serviceName))
    serviceName = config[TelemetryOptions.ServiceNameEnvironmentVariableName];
if (string.IsNullOrWhiteSpace(serviceName))
    serviceName = builder.Environment.ApplicationName;

// Strip the +commithash build metadata; it is noise on a dashboard label.
var serviceVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion.Split('+')[0];

var telemetry = services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithLogging();

// With no endpoint from either source nothing is listening, so skip the exporter rather
// than let it default to localhost:4317 and log a failed export per batch. Credentials
// travel in OTEL_EXPORTER_OTLP_HEADERS, which the SDK reads on its own.
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    var otlpProtocol = telemetryOptions.OtlpProtocol;
    if (string.IsNullOrWhiteSpace(otlpProtocol))
        otlpProtocol = config[TelemetryOptions.ProtocolEnvironmentVariableName];

    telemetry.UseOtlpExporter(
        string.Equals(otlpProtocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc,
        new Uri(otlpEndpoint));
}

// ---- Build ----------------------------------------------------------------

var app = builder.Build();

// ---- Startup tasks --------------------------------------------------------

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// ---- Middleware ------------------------------------------------------------

// First in the pipeline, so everything after it sees the scheme, host and client address
// the original caller used rather than the proxy's.
if (proxyConfig.TrustForwardedHeaders)
    app.UseForwardedHeaders();

// Only rewrites requests that actually carry the prefix, so this is a no-op when the
// proxy strips it before forwarding. Both arrangements work with the same setting.
if (!string.IsNullOrEmpty(proxyConfig.PathBase))
    app.UsePathBase(proxyConfig.PathBase);

app.UseExceptionHandler();
app.UseStatusCodePages();
// No UseDefaultFiles: "/" is left to fall through to MapSpaFallback below, which serves
// the one copy of index.html that has had its <base href> rewritten. Serving it off disk
// here as a default document would hand out the un-rewritten file instead.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"))
});

// Explicit, and deliberately placed here. Left implicit, WebApplication inserts routing at
// the very start of the pipeline, where it would match against the path before UsePathBase
// has stripped the prefix and send every sub-path API call to the SPA fallback instead.
// It also has to stay after the static files above, which serve assets without consulting
// routing at all.
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthchecks", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                error = e.Value.Exception?.Message,
            }),
            duration = report.TotalDuration.TotalMilliseconds
        };

        await ctx.Response.WriteAsJsonAsync(result);
    }
})
.AllowAnonymous();
app.MapPeopleEndpoints();
app.MapAbsencesEndpoints();
app.MapBankHolidaysEndpoints();
app.MapAdminEndpoints();
app.MapConfigEndpoints();
app.MapSpaFallback(proxyConfig.PathBase);

await app.RunAsync();
