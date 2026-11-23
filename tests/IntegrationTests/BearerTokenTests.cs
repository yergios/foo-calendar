using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;

using Api.Configuration;
using Api.Dtos;

using IntegrationTests.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace IntegrationTests;

/// <summary>Auth:Provider=Oidc outside Development, which is how every deployment runs:
/// a request gets in with a bearer token from the configured provider for the configured
/// audience, and with nothing else.</summary>
public sealed class BearerTokenTests : IDisposable
{
    private static readonly Uri People = new("/api/people", UriKind.Relative);
    private static readonly Uri Audit = new("/api/admin/audit", UriKind.Relative);

    private readonly TestIdentityProvider identityProvider = new();
    private readonly TestApp app;

    public BearerTokenTests()
    {
        app = new TestApp { Environment = "Production", IdentityProvider = identityProvider };
    }

    public void Dispose()
    {
        app.Dispose();
        identityProvider.Dispose();
    }

    private async Task<HttpResponseMessage> GetPeopleAsync(string? token)
    {
        using var client = app.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.GetAsync(People);
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task AValidToken_IsLetIn()
    {
        using var response = await GetPeopleAsync(identityProvider.Mint());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // The username claim is read as the token carries it, by the name Auth:ClaimTypes
    // gives, so whoever signs in is the profile they create.
    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task AValidToken_SignsInAsItsUserNameClaim()
    {
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", identityProvider.Mint([new Claim("sub", "jdoe")]));

        using var response = await client.PostAsJsonAsync(People, new UpdatePersonRequest("Jane", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<PersonDto>()).ShouldNotBeNull().Username.ShouldBe("jdoe");
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task NoToken_IsUnauthorized()
    {
        using var response = await GetPeopleAsync(null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task AnExpiredToken_IsUnauthorized()
    {
        // Further back than the validator's five minutes of allowed clock skew.
        using var response = await GetPeopleAsync(identityProvider.Mint(expires: DateTime.UtcNow.AddHours(-1)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task ATokenFromAnotherIssuer_IsUnauthorized()
    {
        using var response = await GetPeopleAsync(identityProvider.Mint(issuer: "https://elsewhere.example.test/"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task ATokenForAnotherAudience_IsUnauthorized()
    {
        using var response = await GetPeopleAsync(identityProvider.Mint(audience: "another-app"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    public async Task ATokenSignedByAnotherKey_IsUnauthorized()
    {
        using var foreignRsa = RSA.Create(2048);
        var foreignKey = new RsaSecurityKey(foreignRsa) { KeyId = "test-key" };

        using var response = await GetPeopleAsync(identityProvider.Mint(signingKey: foreignKey));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // A deployment naming the claim its provider puts roles in. JwtBearer's default mapping
    // would rename "roles" to the long-form role claim type, which no configured name
    // matches, so this holds only while claims keep the names they were issued under.
    private TestApp AppReadingPermissionsFromRoles() =>
        new(("Auth:ClaimTypes:Permissions:0", "roles"))
        {
            Environment = "Production",
            IdentityProvider = identityProvider,
        };

    private static async Task<HttpStatusCode> GetAuditAsync(TestApp rolesApp, string token)
    {
        using var client = rolesApp.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(Audit);
        return response.StatusCode;
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    [Trait("Spec", "AUTH-4")]
    public async Task APermissionClaim_GrantsThePermission_UnderTheTypeTheTokenGivesIt()
    {
        using var rolesApp = AppReadingPermissionsFromRoles();

        var status = await GetAuditAsync(rolesApp, identityProvider.Mint(
            [new Claim("sub", "auditor"), new Claim("roles", "Member"), new Claim("roles", Permissions.AuditRead)]));

        status.ShouldBe(HttpStatusCode.OK);
    }

    // The value under a type the deployment did not name, the long-form role claim type a
    // deployment once had to configure to work around the mapping among them, grants
    // nothing: claim types match exactly.
    [Theory]
    [Trait("Spec", "AUTH-1")]
    [Trait("Spec", "AUTH-4")]
    [InlineData(null)]
    [InlineData("permissions")]
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")]
    public async Task WithoutThePermissionClaim_APermissionRouteIsForbidden(string? otherType)
    {
        using var rolesApp = AppReadingPermissionsFromRoles();
        List<Claim> claims = [new Claim("sub", "member"), new Claim("roles", Permissions.PeopleManage)];
        if (otherType is not null)
            claims.Add(new Claim(otherType, Permissions.AuditRead));

        var status = await GetAuditAsync(rolesApp, identityProvider.Mint(claims));

        status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Spec", "AUTH-1")]
    [Trait("Spec", "PPL-1")]
    public async Task ATokensNameClaims_ReachTheProfileItsHolderCreates()
    {
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            identityProvider.Mint(
                [new Claim("sub", "jdoe"), new Claim("given_name", "Jane"), new Claim("family_name", "Doe")]));

        using var response = await client.PostAsJsonAsync(People, new UpdatePersonRequest(null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stored = app.Query(db => db.People.AsNoTracking().Single(p => p.Username == "jdoe"));
        (stored.FirstName, stored.LastName).ShouldBe(("Jane", "Doe"));
    }

    // A token the provider signed, but without the claim Auth:ClaimTypes:UserName names
    // ("sub" by default): the deployment names the wrong claim. Nothing the caller sends can
    // fix that, so it is a 500 like any other bug, and the log says what to change. It used
    // to be a 409, or a person with no username.
    [Theory]
    [Trait("Spec", "AUTH-9")]
    [InlineData("POST", "/api/people")]
    [InlineData("GET", "/api/people/me")]
    [InlineData("GET", "/api/absences?from=2026-01-01&to=2026-12-31")]
    public async Task ATokenWithoutTheUserNameClaim_IsA500_LoggingTheClaimAndTheSetting(string method, string path)
    {
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", identityProvider.Mint([new Claim("preferred_username", "jdoe")]));
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method == "POST" ? JsonContent.Create(new UpdatePersonRequest("Jane", null)) : null,
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("Auth:ClaimTypes:UserName");
        app.Query(db => db.People.Any()).ShouldBeFalse();
        app.Logs.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Exception != null
            && entry.Exception.Message.Contains("'sub' claim", StringComparison.Ordinal)
            && entry.Exception.Message.Contains("Auth:ClaimTypes:UserName", StringComparison.Ordinal));
    }

    // The same token on a route that doesn't need to know who is asking still gets in.
    [Fact]
    [Trait("Spec", "AUTH-9")]
    public async Task ATokenWithoutTheUserNameClaim_StillReadsWhatNeedsNoMember()
    {
        using var response = await GetPeopleAsync(identityProvider.Mint([new Claim("preferred_username", "jdoe")]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
