using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Api.Configuration;
using Api.Data;
using Api.Dtos;
using Api.Endpoints;
using Api.Infrastructure;
using Api.Interfaces;
using Api.Services;

using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

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

services.AddOptions<NagerOptions>()
    .Bind(config.GetSection(NagerOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Nager:BaseUrl must be configured.")
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

services.AddHostedService<BankHolidaySyncBackgroundService>();

services.AddHttpContextAccessor();
services.AddHttpClient<IBankHolidayService, BankHolidayService>((sp, client) =>
    client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NagerOptions>>().Value.BaseUrl));

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

if (authConfig.Provider == "None" && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Auth:Provider=None is only allowed in Development.");

if (authConfig.Provider != "None")
{
    if (string.IsNullOrWhiteSpace(authConfig.Authority))
        throw new InvalidOperationException("Auth:Authority must be configured.");
    if (string.IsNullOrWhiteSpace(authConfig.Audience))
        throw new InvalidOperationException("Auth:Audience must be configured.");
}

services.AddOptions<AuthOptions>()
    .Bind(config.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();

services.AddAuthorization();

if (authConfig.Provider == "None")
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

// ---- Observability --------------------------------------------------------

// The config value wins when set; otherwise fall back to the well-known env var that
// Azure App Service injects. Blank counts as unset, since appsettings.json ships the
// key with an empty default.
var appInsightsConn = config.GetSection(TelemetryOptions.SectionName)
    .Get<TelemetryOptions>()?.ApplicationInsightsConnectionString;
if (string.IsNullOrWhiteSpace(appInsightsConn))
    appInsightsConn = config[TelemetryOptions.EnvironmentVariableName];

var telemetry = services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: builder.Environment.ApplicationName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

if (!string.IsNullOrWhiteSpace(appInsightsConn))
    telemetry.UseAzureMonitor(options => options.ConnectionString = appInsightsConn);

// ---- Build ----------------------------------------------------------------

var app = builder.Build();

// ---- Startup tasks --------------------------------------------------------

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// ---- Middleware ------------------------------------------------------------

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"))
});
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
app.MapFallbackToFile("index.html");

await app.RunAsync();
