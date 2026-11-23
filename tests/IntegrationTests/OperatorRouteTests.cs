using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Api.Configuration;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

/// <summary>The routes for whoever runs or probes a deployment rather than for its members:
/// security.txt, the health checks and the API documentation.</summary>
public sealed class OperatorRouteTests
{
    private static readonly Uri SecurityTxt = new("/.well-known/security.txt", UriKind.Relative);

    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    [Trait("Spec", "CFG-7")]
    public async Task SecurityTxt_GivesTheSecurityContact_ExpiringAYearAhead()
    {
        using var app = new TestApp(
            ("Team:SecurityContact", "mailto:security@example.test"),
            ("Team:SupportUrl", "https://support.example.test/"));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(SecurityTxt);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/plain");
        var fields = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(": ", 2))
            .ToDictionary(field => field[0], field => field[1], StringComparer.Ordinal);

        fields["Contact"].ShouldBe("mailto:security@example.test");
        var expires = DateTimeOffset.Parse(fields["Expires"], CultureInfo.InvariantCulture);
        expires.ShouldBe(DateTimeOffset.UtcNow.AddYears(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    [Trait("Spec", "CFG-7")]
    public async Task SecurityTxt_FallsBackToTheSupportUrl()
    {
        using var app = new TestApp(
            ("Team:SecurityContact", ""),
            ("Team:SupportUrl", "https://support.example.test/"));
        using var client = app.CreateClient();

        var body = await client.GetStringAsync(SecurityTxt);

        body.ShouldStartWith("Contact: https://support.example.test/\n");
    }

    [Fact]
    [Trait("Spec", "CFG-7")]
    public async Task SecurityTxt_WithNoContactConfigured_IsNotFound()
    {
        using var app = new TestApp(("Team:SecurityContact", ""), ("Team:SupportUrl", ""));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(SecurityTxt);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Anonymous, as a deployment runs it: the probe carries no token.
    [Fact]
    [Trait("Spec", "CFG-8")]
    public async Task TheHealthCheck_AnswersAnyoneWithTheStatusAlone()
    {
        using var identityProvider = new TestIdentityProvider();
        using var app = new TestApp { Environment = "Production", IdentityProvider = identityProvider };
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url("/healthchecks"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"status":"Healthy"}""");
    }

    [Fact]
    [Trait("Spec", "CFG-8")]
    public async Task TheHealthCheckDetail_WithoutAuditRead_IsForbidden()
    {
        using var app = new TestApp
        {
            Permissions = [.. Permissions.All.Where(p => p != Permissions.AuditRead)],
        };
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url("/healthchecks/detail"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Spec", "CFG-8")]
    public async Task TheHealthCheckDetail_WithAuditRead_NamesEachCheck()
    {
        using var app = new TestApp { Permissions = [Permissions.AuditRead] };
        using var client = app.CreateClient();

        using var detail = JsonDocument.Parse(await client.GetStringAsync(Url("/healthchecks/detail")));

        detail.RootElement.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ShouldBe(["sqlite"]);
    }

    private const string OpenApiDocument = "/openapi/v1.json";
    private const string ApiReferenceScript = "/scalar/scalar.js";

    /// <summary>What the API documentation routes answer a signed-in member. Outside
    /// Development the member brings a token: a route that doesn't exist is still behind the
    /// fallback policy, so without one it would be 401 rather than 404.</summary>
    private static async Task<HttpStatusCode[]> ApiDocsStatusAsync(string environment, string? enabled)
    {
        using var identityProvider = new TestIdentityProvider();
        (string, string)[] settings = enabled is null ? [] : [("ApiDocs:Enabled", enabled)];
        using var app = new TestApp(settings)
        {
            Environment = environment,
            IdentityProvider = environment == "Development" ? null : identityProvider,
        };
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", identityProvider.Mint());

        using var document = await client.GetAsync(Url(OpenApiDocument));
        using var reference = await client.GetAsync(Url(ApiReferenceScript));
        return [document.StatusCode, reference.StatusCode];
    }

    // null leaves ApiDocs:Enabled at what the environment's appsettings give it.
    [Theory]
    [Trait("Spec", "CFG-9")]
    [InlineData("Development", null, HttpStatusCode.OK)]
    [InlineData("Development", "false", HttpStatusCode.NotFound)]
    [InlineData("Production", null, HttpStatusCode.NotFound)]
    [InlineData("Production", "true", HttpStatusCode.OK)]
    public async Task TheApiDocs_AreServedOnlyWhenEnabled(string environment, string? enabled, HttpStatusCode expected)
    {
        var statuses = await ApiDocsStatusAsync(environment, enabled);

        statuses.ShouldAllBe(status => status == expected);
    }
}
