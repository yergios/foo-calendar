using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using IntegrationTests.TestSupport;

using Microsoft.AspNetCore.Builder;

using Shouldly;

namespace IntegrationTests;

/// <summary>The headers that limit what a page may do (docs/decisions/0015): the shell's
/// Content-Security-Policy, the API reference's own, and the two headers every response
/// carries.</summary>
public sealed partial class SecurityHeaderTests
{
    private const string Shell =
        "<!doctype html><html><head><base href=\"/\"><title>Foo</title></head><body></body></html>";

    private const string Asset = "/assets/index-3f2a1b.js";

    private static TestApp Served(params (string Key, string Value)[] settings) => new(settings)
    {
        Shell = Shell,
        WebRootFiles = new Dictionary<string, string> { [Asset[1..]] = "console.log('app');" },
    };

    private static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>The policy's directives, by name.</summary>
    private static Dictionary<string, string> Directives(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Content-Security-Policy", out var values)
            .ShouldBeTrue("The response carries no Content-Security-Policy.");

        return values.Single().Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(directive => directive.Split(' ', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "");
    }

    /// <summary>The text of every inline script element in the page.</summary>
    private static List<string> InlineScripts(string html) =>
        [.. InlineScriptPattern().Matches(html).Select(m => m.Groups[1].Value)];

    private static string Sha256(string text) =>
        $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}'";

    // For the configuration served, not a fixed one: the hash is of the inlined bootstrap
    // script, which changes with every configuration value in it.
    [Theory]
    [Trait("Spec", "SEC-1")]
    [InlineData("Acme Engineering")]
    [InlineData("</script><script>alert('x')</script> & <!--")]
    public async Task TheShellsPolicy_AllowsTheInlinedScriptByItsHash_AndNoOtherInlineScript(string orgName)
    {
        using var app = Served(("Team:OrgName", orgName));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url("/"));
        var html = await response.Content.ReadAsStringAsync();

        var script = InlineScripts(html).ShouldHaveSingleItem();
        script.ShouldStartWith("window.__bootstrap=");
        Directives(response)["script-src"].ShouldBe($"'self' {Sha256(script)}");
    }

    [Fact]
    [Trait("Spec", "SEC-1")]
    public async Task TheShellsPolicy_LocksDownEverythingElse()
    {
        using var app = Served();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url("/calendar/2026"));

        var policy = Directives(response);
        policy["default-src"].ShouldBe("'self'");
        policy["style-src"].ShouldBe("'self'");
        policy["object-src"].ShouldBe("'none'");
        policy["base-uri"].ShouldBe("'self'");
        policy["frame-ancestors"].ShouldBe("'none'");
        // The dev identity has no provider to talk to.
        policy["connect-src"].ShouldBe("'self'");
        policy["frame-src"].ShouldBe("'self'");
        policy["form-action"].ShouldBe("'self'");
        policy.Values.ShouldNotContain(value => value.Contains("unsafe", StringComparison.Ordinal));
    }

    // The origin only: the library reaches the discovery document, the token endpoint and
    // the silent sign-in frame under it, not only under the authority's path.
    [Fact]
    [Trait("Spec", "SEC-1")]
    public async Task TheShellsPolicy_AllowsTheIdentityProvidersOrigin_ForConnectionsFramesAndForms()
    {
        using var identityProvider = new TestIdentityProvider();
        using var app = new TestApp
        {
            Shell = Shell,
            IdentityProvider = identityProvider,
        };
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url("/"));

        // The authority is https://idp.example.test/, path and all.
        var policy = Directives(response);
        policy["connect-src"].ShouldBe("'self' https://idp.example.test");
        policy["frame-src"].ShouldBe("'self' https://idp.example.test");
        policy["form-action"].ShouldBe("'self' https://idp.example.test");
    }

    [Theory]
    [Trait("Spec", "SEC-2")]
    [InlineData("/")]
    [InlineData("/calendar/2026")]
    [InlineData("/api/config/bootstrap")]
    [InlineData("/api/people")]
    [InlineData("/api/no-such-route")]
    [InlineData(Asset)]
    [InlineData("/healthchecks")]
    public async Task EveryResponse_IsNosniff_AndSendsNoReferrerToOtherOrigins(string path)
    {
        using var app = Served();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url(path));

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
    }

    // The exception handler clears the headers of the response it takes over.
    [Fact]
    [Trait("Spec", "SEC-2")]
    public async Task AServerError_IsNosniff_AndSendsNoReferrerToOtherOrigins()
    {
        using var app = new TestApp
        {
            Endpoints = routes => routes
                .MapGet("/api/test/throw", string () => throw new InvalidOperationException("Boom."))
                .AllowAnonymous(),
        };
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url("/api/test/throw"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
    }

    // A page of its own, whose inline configuration script is allowed by a nonce issued for
    // that one response, and whose relaxation for styles stays on it.
    [Fact]
    [Trait("Spec", "SEC-4")]
    public async Task TheApiReference_HasAPolicyOfItsOwn_WithAFreshNonceForItsScripts()
    {
        using var app = Served(("ApiDocs:Enabled", "true"));
        using var client = app.CreateClient();

        using var first = await client.GetAsync(Url("/scalar/"));
        using var second = await client.GetAsync(Url("/scalar/"));
        using var shell = await client.GetAsync(Url("/"));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policy = Directives(first);
        var nonce = NoncePattern().Match(policy["script-src"]).Groups[1].Value;
        nonce.ShouldNotBeEmpty();
        policy["script-src"].ShouldBe($"'self' 'nonce-{nonce}' 'unsafe-eval'");
        policy["connect-src"].ShouldBe("'self'");
        policy["object-src"].ShouldBe("'none'");
        policy["frame-ancestors"].ShouldBe("'none'");

        var html = await first.Content.ReadAsStringAsync();
        ScriptTagPattern().Matches(html).Select(m => m.Value)
            .ShouldAllBe(tag => tag.Contains($"nonce=\"{nonce}\"", StringComparison.Ordinal));

        Directives(second)["script-src"].ShouldNotBe(policy["script-src"]);
        Directives(shell)["style-src"].ShouldBe("'self'");
        Directives(shell)["script-src"].ShouldNotContain("unsafe");
    }

    [GeneratedRegex("<script>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InlineScriptPattern();

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex NoncePattern();

    [GeneratedRegex("<script[^>]*>")]
    private static partial Regex ScriptTagPattern();
}
