using System.Net;
using System.Text.RegularExpressions;

using Api.Configuration;

using IntegrationTests.TestSupport;

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
        // The SPA shell, served for any path that is not a file.
        ["* {*path:nonfile}"] = Anonymous,
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

    /// <summary>The app as a deployment runs it: real bearer tokens and every optional route
    /// present. No request here carries a token, so the identity provider is never asked.
    /// </summary>
    private static TestApp Deployment() => new(
        ("Auth:Authority", "https://idp.example.test/"),
        ("Auth:Audience", "foo-calendar"),
        ("Auth:ClientId", "foo-calendar-spa"),
        ("ApiDocs:Enabled", "true"))
    {
        Environment = "Production",
        Shell = Shell,
    };

    private static TestApp MemberHolding(params string[] permissions) =>
        new() { Shell = Shell, Permissions = permissions };

    private sealed record Route(string Key, HttpMethod Method, Uri Url);

    // A request per route, its parameters filled in with values the route accepts. Nothing
    // else about the request has to be valid: authorization answers before the route reads
    // its query or body.
    private static List<Route> Routes(TestApp app) =>
        [.. app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var pattern = endpoint.RoutePattern.RawText;
                var url = new Uri(
                    "/" + RouteParameter().Replace(pattern, p => SampleValue(p.Groups["name"].Value)).TrimStart('/'),
                    UriKind.Relative);
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

                return methods is null
                    ? [new Route($"* {pattern}", HttpMethod.Get, url)]
                    : methods.Select(m => new Route($"{m} {pattern}", new HttpMethod(m), url));
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

    [GeneratedRegex(@"\{\*?(?<name>\w+)[^}]*\}")]
    private static partial Regex RouteParameter();

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, Route route)
    {
        using var request = new HttpRequestMessage(route.Method, route.Url);

        // A route that reads a JSON body only matches a request that says it carries one.
        // Any other request falls through to the SPA fallback, which would answer for it.
        if (route.Method == HttpMethod.Post || route.Method == HttpMethod.Put)
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string AccessOf(Route route) =>
        Access.TryGetValue(route.Key, out var access)
            ? access
            : throw new ShouldAssertException(
                $"{route.Key} has no entry in RouteAccessTests.Access: decide who may call it.");

    [Fact]
    [Trait("Spec", "AUTH-3")]
    [Trait("Spec", "AUTH-6")]
    public void EveryRoute_HasAnEntryInTheAccessTable()
    {
        using var app = Deployment();

        var served = Routes(app).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        served.Where(key => !Access.ContainsKey(key)).ShouldBeEmpty(
            "These routes have no entry in RouteAccessTests.Access: decide who may call them.");
        Access.Keys.Where(key => !served.Contains(key)).ShouldBeEmpty(
            "These entries in RouteAccessTests.Access name routes the app no longer serves.");
    }

    [Fact]
    [Trait("Spec", "AUTH-3")]
    public async Task WithoutCredentials_OnlyTheAnonymousRoutesAnswer()
    {
        using var app = Deployment();
        using var client = app.CreateClient();

        foreach (var route in Routes(app))
        {
            var status = await SendAsync(client, route);

            if (AccessOf(route) == Anonymous)
                status.ShouldNotBeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], route.Key);
            else
                status.ShouldBe(HttpStatusCode.Unauthorized, route.Key);
        }
    }

    [Fact]
    [Trait("Spec", "AUTH-6")]
    public async Task AMemberWithNoPermissions_IsRefusedEveryPermissionRoute_AndNothingElse()
    {
        using var app = MemberHolding();
        using var client = app.CreateClient();

        foreach (var route in Routes(app))
        {
            var status = await SendAsync(client, route);

            if (AccessOf(route) is Anonymous or Member)
                status.ShouldNotBeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], route.Key);
            else
                status.ShouldBe(HttpStatusCode.Forbidden, route.Key);
        }
    }

    public static TheoryData<string> EveryPermission => [.. Permissions.All];

    [Theory]
    [Trait("Spec", "AUTH-6")]
    [MemberData(nameof(EveryPermission))]
    public async Task APermissionRoute_IsRefusedToAMemberHoldingEveryOtherPermission(string permission)
    {
        using var app = MemberHolding([.. Permissions.All.Where(p => p != permission)]);
        using var client = app.CreateClient();

        foreach (var route in Routes(app).Where(r => AccessOf(r) == permission))
            (await SendAsync(client, route)).ShouldBe(HttpStatusCode.Forbidden, route.Key);
    }

    [Theory]
    [Trait("Spec", "AUTH-6")]
    [MemberData(nameof(EveryPermission))]
    public async Task APermissionRoute_AnswersAMemberHoldingThatPermissionAlone(string permission)
    {
        using var app = MemberHolding(permission);
        using var client = app.CreateClient();

        var routes = Routes(app).Where(r => AccessOf(r) == permission).ToList();
        routes.ShouldNotBeEmpty();

        foreach (var route in routes)
            (await SendAsync(client, route)).ShouldNotBe(HttpStatusCode.Forbidden, route.Key);
    }
}
