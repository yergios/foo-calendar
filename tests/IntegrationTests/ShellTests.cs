using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

/// <summary>The SPA shell as the app serves it: index.html rewritten for this deployment,
/// and the hashed files the build puts beside it.</summary>
public sealed class ShellTests
{
    private const string Shell =
        "<!doctype html><html><head><base href=\"/\"><title>Foo</title></head><body></body></html>";

    private const string BootstrapStart = "<script>window.__bootstrap=";
    private const string ScriptEnd = "</script>";

    private static TestApp ServedUnder(string pathBase, params (string Key, string Value)[] settings) =>
        new([("ReverseProxy:PathBase", pathBase), .. settings])
        {
            Shell = Shell,
            WebRootFiles = new Dictionary<string, string>
            {
                ["assets/index-3f2a1b.js"] = "console.log('app');",
                ["favicon.svg"] = "<svg/>",
            },
        };

    private static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>The JSON inlined as window.__bootstrap, exactly as it appears.</summary>
    private static string InlinedBootstrap(string html)
    {
        var start = html.IndexOf(BootstrapStart, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "The shell carries no inlined bootstrap configuration.");
        start += BootstrapStart.Length;
        return html[start..html.IndexOf(ScriptEnd, start, StringComparison.Ordinal)];
    }

    [Theory]
    [Trait("Spec", "CFG-5")]
    [InlineData("/cal/")]
    [InlineData("/cal/calendar/2026")]
    public async Task TheShell_HasThePathBaseAsItsBaseHref_AndIsNeverStored(string path)
    {
        using var app = ServedUnder("/cal");
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url(path));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("<base href=\"/cal/\">");
        html.ShouldNotContain("<base href=\"/\">");
    }

    // The same payload the endpoint serves, so the served SPA and the one under the Vite dev
    // server, which fetches it, start from the same configuration. Equal as JSON, not as
    // text: the two escape differently.
    [Fact]
    [Trait("Spec", "CFG-5")]
    public async Task TheShell_CarriesTheBootstrapConfigurationInline()
    {
        using var app = ServedUnder("/cal", ("Team:OrgName", "Acme Engineering"));
        using var client = app.CreateClient();

        var html = await client.GetStringAsync(Url("/cal/"));
        var endpoint = await client.GetStringAsync(Url("/cal/api/config/bootstrap"));

        var inlined = JsonNode.Parse(InlinedBootstrap(html));
        inlined["orgName"].GetValue<string>().ShouldBe("Acme Engineering");
        JsonNode.DeepEquals(inlined, JsonNode.Parse(endpoint)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "CFG-5")]
    public async Task AFileUnderAssets_IsImmutable_AndOneOutsideItIsNot()
    {
        using var app = ServedUnder("/cal");
        using var client = app.CreateClient();

        using var asset = await client.GetAsync(Url("/cal/assets/index-3f2a1b.js"));
        using var favicon = await client.GetAsync(Url("/cal/favicon.svg"));

        asset.StatusCode.ShouldBe(HttpStatusCode.OK);
        asset.Headers.CacheControl?.ToString().ShouldContain("immutable");
        favicon.StatusCode.ShouldBe(HttpStatusCode.OK);
        (favicon.Headers.CacheControl?.ToString() ?? "").ShouldNotContain("immutable");
    }

    [Fact]
    [Trait("Spec", "CFG-6")]
    public async Task AConfigurationValueThatClosesTheScript_IsInlinedEscaped()
    {
        const string hostile = "</script><script>alert('x')</script> & <!--";
        using var app = ServedUnder("", ("Team:OrgName", hostile));
        using var client = app.CreateClient();

        var html = await client.GetStringAsync(Url("/"));

        // The only closing tag is the one that ends the inlined element itself.
        html.Split(ScriptEnd).Length.ShouldBe(2);
        var json = InlinedBootstrap(html);
        json.ShouldNotContain("<");
        json.ShouldNotContain(">");
        json.ShouldNotContain("&");

        using var inlined = JsonDocument.Parse(json);
        inlined.RootElement.GetProperty("orgName").GetString().ShouldBe(hostile);
    }
}
