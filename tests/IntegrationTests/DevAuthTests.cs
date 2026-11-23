using System.Net;
using System.Net.Http.Json;

using Api.Dtos;

using IntegrationTests.TestSupport;

using Microsoft.Extensions.Logging;

using Shouldly;

namespace IntegrationTests;

/// <summary>Auth:Provider=None, the dev bypass appsettings.Development.json turns on.</summary>
public sealed class DevAuthTests
{
    // Creating one's own profile takes the username from the signed-in identity, so the
    // profile says who the request was authenticated as. No token is sent.
    [Fact]
    [Trait("Spec", "AUTH-2")]
    public async Task EveryRequest_IsAuthenticatedAsTheConfiguredDevUser()
    {
        using var app = new TestApp(("Auth:DevUser:Username", "someone-else"));
        using var client = app.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/people", UriKind.Relative), new UpdatePersonRequest("Someone", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<PersonDto>()).ShouldNotBeNull();
        created.Username.ShouldBe("someone-else");
    }

    [Theory]
    [Trait("Spec", "AUTH-2")]
    [InlineData("true", HttpStatusCode.OK)]
    [InlineData("false", HttpStatusCode.Forbidden)]
    public async Task TheDevUser_HoldsThePermissionsItIsConfiguredWith(
        string grantAll, HttpStatusCode expected)
    {
        using var app = new TestApp(("Auth:DevUser:GrantAllPermissions", grantAll));
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/admin/audit", UriKind.Relative));

        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [Trait("Spec", "AUTH-2")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void OutsideDevelopment_StopsStartup(string environment)
    {
        using var app = new TestApp(("Auth:Provider", "None")) { Environment = environment };

        Should.Throw<Exception>(() => app.Services)
            .Message.ShouldContain("Auth:Provider=None is only allowed in Development.");
    }

    [Fact]
    [Trait("Spec", "AUTH-2")]
    public void Startup_WarnsNamingTheIdentityAndItsPermissions()
    {
        using var app = new TestApp(
            ("Auth:DevUser:Username", "someone-else"),
            ("Auth:DevUser:GrantAllPermissions", "false"),
            ("Auth:DevUser:Permissions:0", "AuditRead"));

        _ = app.Services;

        var warning = app.Logs.Entries
            .Where(e => e.Level == LogLevel.Warning
                && e.Message.Contains("Auth:Provider=None", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        warning.Message.ShouldContain("'someone-else'");
        warning.Message.ShouldContain("[AuditRead]");
    }
}
