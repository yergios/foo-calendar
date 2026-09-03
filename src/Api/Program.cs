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
using Microsoft.AspNetCore.Authorization;
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

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var services = builder.Services;

// ---- Options --------------------------------------------------------------

services.AddOptions<TeamOptions>()
    .Bind(config.GetSection(TeamOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.HomeRegion),
        "Team:HomeRegion must be configured, e.g. \"CH-ZH\".")
    .Validate(o => string.IsNullOrWhiteSpace(o.HomeRegion)
            || Regex.IsMatch(o.HomeRegion, ValidationPatterns.RegionCode),
        "Team:HomeRegion must be an ISO 3166-2 code, e.g. \"CH\" or \"CH-ZH\".")
    .Validate(o => !string.IsNullOrWhiteSpace(o.HomeTimeZone),
        "Team:HomeTimeZone must be configured, e.g. \"Europe/Zurich\".")
    .Validate(o => string.IsNullOrWhiteSpace(o.HomeTimeZone)
            || TimeZoneInfo.TryFindSystemTimeZoneById(o.HomeTimeZone, out _),
        "Team:HomeTimeZone must be a valid IANA timezone id, e.g. \"Europe/Zurich\".")
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

services.AddOptions<AbsenceApprovalOptions>()
    .Bind(config.GetSection(AbsenceApprovalOptions.SectionName))
    .ValidateOnStart();

services.AddOptions<ApiDocsOptions>()
    .Bind(config.GetSection(ApiDocsOptions.SectionName))
    .Validate(o => o.RoutePrefix.StartsWith('/'), "ApiDocs:RoutePrefix must start with '/'.")
    .ValidateOnStart();

services.AddOptions<RetentionOptions>()
    .Bind(config.GetSection(RetentionOptions.SectionName))
    .Validate(o => o.AuditEntryDays >= 0, "Retention:AuditEntryDays must not be negative.")
    .Validate(o => o.NotificationDays >= 0, "Retention:NotificationDays must not be negative.")
    .Validate(o => o.AbsenceDays >= 0, "Retention:AbsenceDays must not be negative.")
    .Validate(o => o.SweepInterval > TimeSpan.Zero, "Retention:SweepInterval must be positive.")
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

// A year's absences run several hundred KB uncompressed for a real team - gzip/brotli cut
// that roughly 5x on the wire, which measured as the dominant cost on anything but a fast
// connection. EnableForHttps is safe here: nothing in these responses is a secret sitting
// alongside attacker-influenced content, which is what the BREACH-style risk it guards
// against actually requires.
services.AddResponseCompression(o => o.EnableForHttps = true);

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
services.AddScoped<ITeamRegionService, TeamRegionService>();

// Singleton: it reads a static name table off disk once and answers from memory after.
services.AddSingleton<RegionLabels>();

services.AddScoped<IAbsenceService, AbsenceService>();
services.AddScoped<IPersonService, PersonService>();
services.AddScoped<ICurrentPersonAccessor, CurrentPersonAccessor>();
services.AddScoped<IAuditLog, AuditLog>();
services.AddScoped<INotificationService, NotificationService>();

// ---- Retention ------------------------------------------------------------

// Always on: the defaults keep audit entries a year and notifications 90 days, and a
// deployment that wants either kept forever says so with 0 rather than by the job not
// existing. Absences are the one thing it leaves alone by default, see RetentionOptions.
services.AddScoped<IRetentionService, RetentionService>();
services.AddHostedService<RetentionBackgroundService>();

// ---- API reference --------------------------------------------------------

// Read unbound, like the auth section below: whether to generate a document at all is
// decided before the container exists, and generating one nobody serves costs startup time
// and hands a surprise to anyone who later opens the route expecting it to be off.
var apiDocsEnabled = config.GetSection(ApiDocsOptions.SectionName)
    .GetValue<bool>(nameof(ApiDocsOptions.Enabled));

if (apiDocsEnabled)
    services.AddOpenApi(o => o.AddDocumentTransformer<ApiDocumentTransformer>());

// ---- Validation & Error handling ------------------------------------------

services.AddValidation();
services.AddProblemDetails();
services.AddExceptionHandler<DomainExceptionHandler>();

// ---- Health checks --------------------------------------------------------

services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("sqlite",
        failureStatus: HealthStatus.Degraded);

// ---- Auth -----------------------------------------------------------------

// Settings that changed shape. Binding would otherwise quietly ignore the old form and
// leave a permission gated on nothing, so say exactly what to change instead. These checks
// can go once deployments have moved over.
if (config[$"{AuthOptions.SectionName}:AdminPermission"] is not null
    || config.GetSection($"{AuthOptions.SectionName}:AdminPermissions").Exists())
    throw new InvalidOperationException(
        "Auth:AdminPermissions is now the per-permission map Auth:Permissions, e.g. " +
        $"{{ \"{Permissions.PeopleManage}\": [\"APP_FOO_CALENDAR_ADMIN\"] }} or " +
        $"Auth__Permissions__{Permissions.PeopleManage}__0=APP_FOO_CALENDAR_ADMIN. Listing the " +
        $"same value under every permission ({string.Join(", ", Permissions.All)}) reproduces " +
        "the single admin role it replaced.");

if (config[$"{AuthOptions.SectionName}:ClaimTypes:Permissions"] is not null)
    throw new InvalidOperationException(
        "Auth:ClaimTypes:Permissions is now a list, e.g. [\"roles\"] or " +
        "Auth__ClaimTypes__Permissions__0=roles.");

var isDevelopment = builder.Environment.IsDevelopment();

services.AddOptions<AuthOptions>()
    .Bind(config.GetSection(AuthOptions.SectionName))
    .PostConfigure(o => o.RemoveBlankEntries())
    .Validate(o => o.Provider is AuthOptions.ProviderOidc or AuthOptions.ProviderNone,
        $"Auth:Provider must be one of {AuthOptions.ProviderOidc}, {AuthOptions.ProviderNone}.")
    .Validate(o => o.Provider != AuthOptions.ProviderNone || isDevelopment,
        "Auth:Provider=None is only allowed in Development.")
    .Validate(o => o.Provider == AuthOptions.ProviderNone || !string.IsNullOrWhiteSpace(o.Authority),
        "Auth:Authority must be configured.")
    .Validate(o => o.Provider == AuthOptions.ProviderNone || !string.IsNullOrWhiteSpace(o.Audience),
        "Auth:Audience must be configured.")
    // The API never uses ClientId itself, but the SPA can't start a login without it, and
    // failing at startup beats a frontend that only breaks once someone tries to sign in.
    .Validate(o => o.Provider == AuthOptions.ProviderNone || !string.IsNullOrWhiteSpace(o.ClientId),
        "Auth:ClientId must be configured.")
    // Fail closed: a permission nobody can hold rejects every request against it with no
    // indication why, and an accidental "grant everyone" is worse still. Requiring all of
    // them also means adding a permission is a deliberate deployment decision rather than
    // something that silently starts out unreachable after an upgrade.
    .Validate(o => Permissions.All.All(p => o.ValuesFor(p).Count > 0),
        $"Auth:Permissions must list at least one claim value for each of: {string.Join(", ", Permissions.All)}.")
    // A typo would otherwise sit there granting nothing, which reads exactly like a
    // provider that isn't sending the claim.
    .Validate(o => o.Permissions.Keys.All(name => Permissions.Known.Contains(name, StringComparer.OrdinalIgnoreCase)),
        $"Auth:Permissions names a permission that doesn't exist. The permissions are: {string.Join(", ", Permissions.Known)}.")
    .Validate(o => o.ClaimTypes.Permissions.Count > 0,
        "Auth:ClaimTypes:Permissions must list at least one claim name.")
    .ValidateOnStart();

// Read once more, unbound, because which authentication scheme to register has to be
// decided before the container exists. Anything invalid in here is rejected by the
// validation above before the app serves a request.
var authConfig = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
    ?? throw new InvalidOperationException("Auth options are not configured.");
authConfig.RemoveBlankEntries();

services.AddAuthorization(options =>
{
    // Closed by default: an endpoint added without RequireAuthorization is protected
    // rather than public, so forgetting to say anything fails safe. The handful of
    // genuinely public routes opt out with AllowAnonymous.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // One policy per permission, named after it, so an endpoint asks for the permission it
    // needs by name and nothing here has to be touched to add another one. Known, not All:
    // an optional permission left unconfigured still gets a policy, just one nobody's
    // claims satisfy, which is exactly the "nobody can do this yet" an opt-in feature
    // should start in.
    //
    // Not RequireClaim: that pairs one claim name with its accepted values, and this has to
    // hold across several of each.
    foreach (var permission in Permissions.Known)
    {
        var accepted = authConfig.ValuesFor(permission);

        options.AddPolicy(permission, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.HasAnyPermission(
                authConfig.ClaimTypes.Permissions, accepted)));
    }
});

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

// No ValidateOnStart here: what has to be well-formed is the effective endpoint and
// protocol, which merge this section with the OTEL_* environment variables the bound
// options never see. Both are checked where they are resolved, below.
services.AddOptions<TelemetryOptions>()
    .Bind(config.GetSection(TelemetryOptions.SectionName));

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
    if (string.IsNullOrWhiteSpace(otlpProtocol))
        otlpProtocol = TelemetryOptions.ProtocolGrpc;

    // None of this is needed for the app to work, but a typo should say so plainly rather
    // than silently export over the wrong protocol or throw from inside the exporter.
    var httpProtobuf = string.Equals(
        otlpProtocol, TelemetryOptions.ProtocolHttpProtobuf, StringComparison.OrdinalIgnoreCase);

    if (!httpProtobuf && !string.Equals(
            otlpProtocol, TelemetryOptions.ProtocolGrpc, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            $"OTLP protocol must be one of {TelemetryOptions.ProtocolGrpc}, " +
            $"{TelemetryOptions.ProtocolHttpProtobuf}; got '{otlpProtocol}'.");

    if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri))
        throw new InvalidOperationException(
            $"OTLP endpoint must be an absolute URL; got '{otlpEndpoint}'.");

    telemetry.UseOtlpExporter(
        httpProtobuf ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc,
        otlpUri);
}

// ---- Build ----------------------------------------------------------------

var app = builder.Build();

// ---- Startup tasks --------------------------------------------------------

// Loud on purpose. This is the one configuration where every request is authenticated as
// a fixed identity with no token involved; the startup check above keeps it out of other
// environments, but ASPNETCORE_ENVIRONMENT is the only thing that does, so an accidental
// Development deployment should be obvious in the logs rather than silent.
if (authConfig.Provider == AuthOptions.ProviderNone)
    app.Logger.LogWarning(
        "Auth:Provider=None: authentication is bypassed and every request is '{Username}' "
        + "holding [{Permissions}]. Development only, never deploy this.",
        authConfig.DevUser.Username,
        string.Join(", ", authConfig.EffectiveDevPermissions()));

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

// As early as the framework allows, per Microsoft's own guidance for this middleware -
// everything after it should be compressed, not just the API responses it was added for.
app.UseResponseCompression();

app.UseExceptionHandler();
app.UseStatusCodePages();
// No UseDefaultFiles: "/" is left to fall through to MapSpaFallback below, which serves
// the one copy of index.html that has had its <base href> rewritten. Serving it off disk
// here as a default document would hand out the un-rewritten file instead.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot")),
    OnPrepareResponse = ctx =>
    {
        // Vite writes every bundled file under assets/ with a content hash in its name, so
        // a changed file always ships under a new URL. That is what makes immutable safe:
        // the shell (served no-store by SpaShell) is the only mutable entry point, and it
        // names which hashed files to load. Without this, browsers revalidate each asset
        // with a 304 round trip on every cold load. Path is post-UsePathBase, so this
        // matches under sub-path deployments too. favicon.svg is unhashed and gets nothing.
        if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
});

// Explicit, and deliberately placed here. Left implicit, WebApplication inserts routing at
// the very start of the pipeline, where it would match against the path before UsePathBase
// has stripped the prefix and send every sub-path API call to the SPA fallback instead.
// It also has to stay after the static files above, which serve assets without consulting
// routing at all.
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Anonymous, because this is what the container's HEALTHCHECK and orchestrator probes
// call. It still runs every check, so the status code means what it always did, but the
// body carries nothing else: check descriptions and exception messages spell out database
// paths and connection details, and this endpoint is reachable by anyone.
app.MapHealthChecks("/healthchecks", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
    }
})
.AllowAnonymous();

// The per-check detail an operator needs to tell *why* the summary above went red, behind
// a permission for the same reason the summary omits it. AuditRead rather than a
// permission of its own: both are the same read-only window into what the deployment is
// doing, held by whoever is expected to look into it when something is off.
app.MapHealthChecks("/healthchecks/detail", new HealthCheckOptions
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
.RequireAuthorization(Permissions.AuditRead);
app.MapPeopleEndpoints();
app.MapAbsencesEndpoints();
app.MapBankHolidaysEndpoints();
app.MapAdminEndpoints();
app.MapConfigEndpoints();
app.MapRegionEndpoints();
app.MapNotificationsEndpoints();

// Both anonymous, and both off outside Development by default: a browser opening either
// carries no bearer token, so requiring one would serve a 401 to the only thing that ever
// opens them. They expose the shape of the API and never its contents, see ApiDocsOptions.
if (apiDocsEnabled)
{
    var apiDocs = app.Services.GetRequiredService<IOptions<ApiDocsOptions>>().Value;

    app.MapOpenApi().AllowAnonymous();

    // The document URL is left at its default, which is relative: Scalar's own loader
    // resolves it against the page's path minus the UI's prefix, so it lands on
    // "<pathbase>/openapi/v1.json" at the root and behind a sub-path alike. Spelling it out
    // here, with or without the path base, breaks one of those two.
    app.MapScalarApiReference(apiDocs.RoutePrefix, options => options
        .WithTitle($"{ProductInfo.Name} API"))
        .AllowAnonymous();
}

app.MapSpaFallback(proxyConfig.PathBase);

await app.RunAsync();
