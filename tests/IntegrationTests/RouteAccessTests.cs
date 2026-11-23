using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.RegularExpressions;

using Api.Configuration;
using Api.Interfaces;
using Api.Services;

using IntegrationTests.TestSupport;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace IntegrationTests;

/// <summary>
/// Who may call every route the app serves, read from the app's own route table rather
/// than from a list kept by hand, so a route added without an entry in <see cref="Access"/>
/// fails here until someone decides who it is for.
/// </summary>
public sealed partial class RouteAccessTests
{
    private const string Anonymous = "anonymous";
    private const string Member = "member";

    /// <summary>Every route, keyed as "METHOD pattern" the way the route table spells it,
    /// with "*" for a route that answers any method. The value is <see cref="Anonymous"/>
    /// (AUTH-3's list), <see cref="Member"/>, or the permission the route requires.</summary>
    private static readonly Dictionary<string, string> Access = new(StringComparer.Ordinal)
    {
        // The SPA shell, served for any page that is not a file.
        ["GET {*path:nonfile}"] = Anonymous,
        ["HEAD {*path:nonfile}"] = Anonymous,
        ["GET /api/config/bootstrap"] = Anonymous,
        ["* /healthchecks"] = Anonymous,
        ["GET /.well-known/security.txt"] = Anonymous,
        // The OpenAPI document and the API reference, present only with ApiDocs:Enabled.
        ["GET /openapi/{documentName}.json"] = Anonymous,
        ["GET /scalar/{documentName?}"] = Anonymous,
        ["GET /scalar/scalar.js"] = Anonymous,
        ["GET /scalar/scalar.aspnetcore.js"] = Anonymous,
        ["GET /scalar/favicon.svg"] = Anonymous,

        ["GET /api/config"] = Member,
        ["POST /api/people/"] = Member,
        ["GET /api/people/"] = Member,
        ["GET /api/people/paged"] = Member,
        ["GET /api/people/me"] = Member,
        ["PUT /api/people/me"] = Member,
        ["DELETE /api/people/me"] = Member,
        ["GET /api/people/me/stats"] = Member,
        ["GET /api/absences/"] = Member,
        ["POST /api/absences/"] = Member,
        ["PUT /api/absences/{id:int}"] = Member,
        ["DELETE /api/absences/{id:int}"] = Member,
        ["GET /api/bank-holidays/catalog"] = Member,
        ["POST /api/bank-holidays/"] = Member,
        ["PUT /api/bank-holidays/{id:int}"] = Member,
        ["DELETE /api/bank-holidays/{id:int}"] = Member,
        ["GET /api/regions/countries"] = Member,
        ["GET /api/regions/{countryCode}"] = Member,
        // An API path no other route takes, kept from falling through to the shell.
        ["GET /api/{**path}"] = Member,
        ["HEAD /api/{**path}"] = Member,

        ["POST /api/admin/people"] = Permissions.PeopleManage,
        ["POST /api/admin/people/import"] = Permissions.PeopleManage,
        ["PUT /api/admin/people/{id:int}"] = Permissions.PeopleManage,
        ["DELETE /api/admin/people/{id:int}"] = Permissions.PeopleManage,
        ["GET /api/admin/people/stats"] = Permissions.ReportsRead,
        ["POST /api/admin/bank-holidays/sync"] = Permissions.HolidaysSync,
        ["GET /api/admin/audit"] = Permissions.AuditRead,
        ["* /healthchecks/detail"] = Permissions.AuditRead,
    };

    // A shell to serve, so the SPA fallback route exists whether or not a frontend build
    // happens to sit in the Api project's wwwroot.
    private const string Shell =
        "<!doctype html><html><head><base href=\"/\"></head><body></body></html>";

    /// <summary>
    /// Each holiday configuration a deployment can run, because what the app maps may depend
    /// on it: None, which is what TestApp starts with unless told otherwise, and a source,
    /// the shipped default. A route mapped only with a source would otherwise never be
    /// enumerated here at all.
    /// </summary>
    private static readonly string[] HolidaySources = [HolidayOptions.SourceNone, HolidayOptions.SourceNager];

    public static TheoryData<string> EachHolidaySource => [.. HolidaySources];

    public static TheoryData<string, string> EveryPermissionUnderEachHolidaySource
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var source in HolidaySources)
            {
                foreach (var permission in Permissions.All)
                    data.Add(permission, source);
            }

            return data;
        }
    }

    // With a source configured, nothing may leave the machine: the source is the stand-in
    // None uses, which answers that there is nothing to sync, and the scheduled sync is taken
    // out so nothing calls it unasked. Both are no-ops under None.
    private static void KeepTheSourceLocal(IServiceCollection services)
    {
        services.AddSingleton<IHolidaySource, DisabledHolidaySource>();
        var scheduled = services.SingleOrDefault(d => d.ImplementationType == typeof(BankHolidaySyncBackgroundService));
        if (scheduled is not null)
            services.Remove(scheduled);
    }

    /// <summary>The app as a deployment runs it: real bearer tokens and every optional route
    /// present. No request here carries a token, so the identity provider is never asked.
    /// </summary>
    private static TestApp Deployment(string holidaySource) => new(
        ("Auth:Authority", "https://idp.example.test/"),
        ("Auth:Audience", "foo-calendar"),
        ("Auth:ClientId", "foo-calendar-spa"),
        ("ApiDocs:Enabled", "true"),
        ("Holidays:Source", holidaySource))
    {
        Environment = "Production",
        Shell = Shell,
        ServiceOverrides = KeepTheSourceLocal,
    };

    private static TestApp MemberHolding(string holidaySource, params string[] permissions) =>
        new(("Holidays:Source", holidaySource))
        {
            Shell = Shell,
            Permissions = permissions,
            ServiceOverrides = KeepTheSourceLocal,
        };

    /// <summary><paramref name="TakesBody"/>: the route reads a JSON body, which routing
    /// records as the content types it accepts.</summary>
    private sealed record Route(string Key, HttpMethod Method, Uri Url, bool TakesBody);

    // A request per route, its parameters filled in with values the route accepts. Nothing
    // else about the request has to be valid: authorization answers before the route reads
    // its query or body.
    private static List<Route> Routes(TestApp app) =>
        [.. app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var pattern = endpoint.RoutePattern.RawText.ShouldNotBeNull();
                var url = new Uri(
                    "/" + RouteParameter().Replace(pattern, p => SampleValue(p.Groups["name"].Value)).TrimStart('/'),
                    UriKind.Relative);
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                var takesBody = endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is not null;

                return methods is null
                    ? [new Route($"* {pattern}", HttpMethod.Get, url, takesBody)]
                    : methods.Select(m => new Route($"{m} {pattern}", new HttpMethod(m), url, takesBody));
            })];

    private static string SampleValue(string parameter) => parameter switch
    {
        "id" => "1",
        "countryCode" => "CH",
        "documentName" => "v1",
        "path" => "calendar",
        _ => throw new InvalidOperationException(
            $"No sample value for route parameter '{parameter}': add one to {nameof(SampleValue)}."),
    };

    [GeneratedRegex(@"\{\*{0,2}(?<name>\w+)[^}]*\}")]
    private static partial Regex RouteParameter();

    // The query a permission route requires, so that one let in is answered by the route
    // itself rather than by binding's 400 for the missing parameter, which would say
    // nothing about whether the route runs for that member.
    private static readonly Dictionary<string, string> RequiredQuery = new(StringComparer.Ordinal)
    {
        ["GET /api/admin/people/stats"] = "year=2026",
        ["POST /api/admin/bank-holidays/sync"] = "year=2026",
    };

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, Route route)
    {
        // No body: routing passes a request naming no content type to a route that reads
        // JSON, whose authorization answers first. One naming another type would get
        // routing's 415 instead, which says nothing about who may call the route.
        var url = RequiredQuery.TryGetValue(route.Key, out var query)
            ? new Uri($"{route.Url}?{query}", UriKind.Relative)
            : route.Url;
        using var request = new HttpRequestMessage(route.Method, url);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string AccessOf(Route route) =>
        Access.TryGetValue(route.Key, out var access)
            ? access
            : throw new ShouldAssertException(
                $"{route.Key} has no entry in RouteAccessTests.Access: decide who may call it.");

    // Every route any supported configuration maps. Deployment enables the API docs, and
    // each holiday configuration is started in turn.
    [Fact]
    [Trait("Spec", "AUTH-3")]
    [Trait("Spec", "AUTH-6")]
    public void EveryRoute_HasAnEntryInTheAccessTable()
    {
        var served = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in HolidaySources)
        {
            using var app = Deployment(source);
            served.UnionWith(Routes(app).Select(r => r.Key));
        }

        served.Where(key => !Access.ContainsKey(key)).ShouldBeEmpty(
            "These routes have no entry in RouteAccessTests.Access: decide who may call them.");
        Access.Keys.Where(key => !served.Contains(key)).ShouldBeEmpty(
            "These entries in RouteAccessTests.Access name routes the app no longer serves.");
    }

    [Theory]
    [Trait("Spec", "AUTH-3")]
    [MemberData(nameof(EachHolidaySource))]
    public async Task WithoutCredentials_OnlyTheAnonymousRoutesAnswer(string holidaySource)
    {
        using var app = Deployment(holidaySource);
        using var client = app.CreateClient();

        foreach (var route in Routes(app))
        {
            var status = await SendAsync(client, route);

            if (AccessOf(route) == Anonymous)
                ShouldBeAnswered(status, route);
            else
                status.ShouldBe(HttpStatusCode.Unauthorized, route.Key);
        }
    }

    [Theory]
    [Trait("Spec", "AUTH-6")]
    [MemberData(nameof(EachHolidaySource))]
    public async Task AMemberWithNoPermissions_IsRefusedEveryPermissionRoute_AndNothingElse(string holidaySource)
    {
        using var app = MemberHolding(holidaySource);
        using var client = app.CreateClient();

        foreach (var route in Routes(app))
        {
            var status = await SendAsync(client, route);

            if (AccessOf(route) is Anonymous or Member)
                ShouldBeAnswered(status, route);
            else
                status.ShouldBe(HttpStatusCode.Forbidden, route.Key);
        }
    }

    [Theory]
    [Trait("Spec", "AUTH-6")]
    [MemberData(nameof(EveryPermissionUnderEachHolidaySource))]
    public async Task APermissionRoute_IsRefusedToAMemberHoldingEveryOtherPermission(string permission, string holidaySource)
    {
        using var app = MemberHolding(holidaySource, [.. Permissions.All.Where(p => p != permission)]);
        using var client = app.CreateClient();

        foreach (var route in Routes(app).Where(r => AccessOf(r) == permission))
            (await SendAsync(client, route)).ShouldBe(HttpStatusCode.Forbidden, route.Key);
    }

    [Theory]
    [Trait("Spec", "AUTH-6")]
    [MemberData(nameof(EveryPermissionUnderEachHolidaySource))]
    public async Task APermissionRoute_AnswersAMemberHoldingThatPermissionAlone(string permission, string holidaySource)
    {
        using var app = MemberHolding(holidaySource, permission);
        using var client = app.CreateClient();

        var routes = Routes(app).Where(r => AccessOf(r) == permission).ToList();
        routes.ShouldNotBeEmpty();

        foreach (var route in routes)
            ShouldBeAnswered(await SendAsync(client, route), route);
    }

    // With the body optional, as it was while nullable reference types were off, a route
    // reading one got null and answered 500. Development included: there the framework
    // throws rather than answers, and the 400 has to survive the exception handler.
    [Theory]
    [Trait("Spec", "ERR-1")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ABodyTakingRoute_SentNoBody_IsABadRequest(string environment)
    {
        using var identityProvider = new TestIdentityProvider();
        using var app = environment == "Development"
            ? MemberHolding(HolidayOptions.SourceNone, [.. Permissions.All])
            : new TestApp { Environment = environment, Shell = Shell, IdentityProvider = identityProvider };
        using var client = app.CreateClient();

        // Every permission, under the claim name appsettings.json reads them from.
        if (environment != "Development")
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                identityProvider.Mint(
                    [new Claim("sub", "token-user"), .. Permissions.All.Select(p => new Claim("permissions", p))]));

        var routes = Routes(app).Where(r => r.TakesBody && r.Url.OriginalString.StartsWith("/api/", StringComparison.Ordinal)).ToList();
        routes.ShouldNotBeEmpty();

        foreach (var route in routes)
            (await SendAsync(client, route)).ShouldBe(HttpStatusCode.BadRequest, route.Key);
    }

    // Let in, and then answered by the route: not refused, and not failed either, which a
    // check for "not 403" alone would also pass.
    private static void ShouldBeAnswered(HttpStatusCode status, Route route)
    {
        status.ShouldNotBeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], route.Key);
        ((int)status).ShouldBeLessThan(500, route.Key);
    }
}
