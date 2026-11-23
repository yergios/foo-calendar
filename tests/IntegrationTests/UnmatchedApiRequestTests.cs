using System.Net;
using System.Text;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

/// <summary>
/// API requests no route takes, with a shell present to be wrongly served instead. Each
/// fails as an API call, never as a 200 page: unauthenticated, 401 first; signed in, what
/// routing answers when nothing matches (404), when the route takes another method (405),
/// or another content type (415). Pages the SPA routes itself still get the shell.
/// </summary>
public sealed class UnmatchedApiRequestTests
{
    private const string Shell =
        "<!doctype html><html><head><base href=\"/\"></head><body></body></html>";

    private const string ProblemJson = "application/problem+json";

    /// <summary>The app as a deployment runs it, with nobody signed in.</summary>
    private static TestApp Deployment(params (string Key, string Value)[] settings) => new(
        [
            ("Auth:Authority", "https://idp.example.test/"),
            ("Auth:Audience", "foo-calendar"),
            ("Auth:ClientId", "foo-calendar-spa"),
            .. settings,
        ])
    {
        Environment = "Production",
        Shell = Shell,
    };

    private static TestApp SignedInMember() => new() { Shell = Shell, Permissions = [] };

    private static async Task<HttpResponseMessage> SendAsync(
        TestApp app, string method, string path, string? contentType = null)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (contentType is not null)
            request.Content = new StringContent("not json", Encoding.UTF8, contentType);

        return await client.SendAsync(request);
    }

    [Theory]
    [Trait("Spec", "AUTH-1")]
    [Trait("Spec", "AUTH-3")]
    [InlineData("POST", "/api/absences", "text/plain")]
    [InlineData("POST", "/api/absences", null)]
    [InlineData("PUT", "/api/people/me", "text/plain")]
    [InlineData("PATCH", "/api/absences/1", null)]
    [InlineData("GET", "/api/nope", null)]
    [InlineData("GET", "/api", null)]
    [InlineData("DELETE", "/api/absences/abc", null)]
    public async Task WithoutCredentials_AnyApiRequest_IsUnauthorized(string method, string path, string? contentType)
    {
        using var app = Deployment();

        using var response = await SendAsync(app, method, path, contentType);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ProblemJson);
    }

    // Behind a path base too, where the prefix is stripped before routing sees /api.
    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task WithoutCredentials_AnUnmatchedApiPathUnderThePathBase_IsUnauthorized()
    {
        using var app = Deployment(("ReverseProxy:PathBase", "/cal"));

        using var response = await SendAsync(app, "GET", "/cal/api/nope");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [Trait("Spec", "AUTH-3")]
    [InlineData("GET", "/api/nope", null, HttpStatusCode.NotFound)]
    [InlineData("GET", "/api", null, HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/api/absences/abc", null, HttpStatusCode.NotFound)]
    [InlineData("PATCH", "/api/absences/1", null, HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/api/absences", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("PUT", "/api/people/me", "text/plain", HttpStatusCode.UnsupportedMediaType)]
    public async Task SignedIn_AnApiRequestNoRouteTakes_AnswersAsRoutingDoes(
        string method, string path, string? contentType, HttpStatusCode expected)
    {
        using var app = SignedInMember();

        using var response = await SendAsync(app, method, path, contentType);

        response.StatusCode.ShouldBe(expected);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ProblemJson);
    }

    [Theory]
    [Trait("Spec", "AUTH-3")]
    [InlineData("GET", "/")]
    [InlineData("GET", "/calendar/2026")]
    [InlineData("HEAD", "/calendar/2026")]
    [InlineData("GET", "/apis")]
    public async Task WithoutCredentials_APageTheSpaRoutes_GetsTheShell(string method, string path)
    {
        using var app = Deployment();

        using var response = await SendAsync(app, method, path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }
}
