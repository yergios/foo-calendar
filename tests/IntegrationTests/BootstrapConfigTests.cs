using System.Text.Json;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

public sealed class BootstrapConfigTests
{
    private static async Task<JsonElement> AuthConfigAsync(TestApp app)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/config/bootstrap", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("auth").Clone();
    }

    // The SPA hands this to the provider when signing out; relative by default so one
    // deployment works on any hostname, like the sign-in redirect.
    [Fact]
    [Trait("Spec", "AUTH-8")]
    public async Task PostLogoutRedirectUri_DefaultsToTheAppsRoot()
    {
        using var app = new TestApp();

        var auth = await AuthConfigAsync(app);

        auth.GetProperty("postLogoutRedirectUri").GetString().ShouldBe("/");
    }

    [Fact]
    [Trait("Spec", "AUTH-8")]
    public async Task PostLogoutRedirectUri_IsServedAsConfigured()
    {
        using var app = new TestApp(("Auth:PostLogoutRedirectUri", "https://intra.example/calendar/"));

        var auth = await AuthConfigAsync(app);

        auth.GetProperty("postLogoutRedirectUri").GetString().ShouldBe("https://intra.example/calendar/");
    }
}
