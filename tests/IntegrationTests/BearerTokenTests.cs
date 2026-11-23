using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;

using Api.Dtos;

using IntegrationTests.TestSupport;

using Microsoft.IdentityModel.Tokens;

using Shouldly;

namespace IntegrationTests;

/// <summary>Auth:Provider=Oidc outside Development, which is how every deployment runs:
/// a request gets in with a bearer token from the configured provider for the configured
/// audience, and with nothing else.</summary>
public sealed class BearerTokenTests : IDisposable
{
    private static readonly Uri People = new("/api/people", UriKind.Relative);

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

    private async Task<HttpResponseMessage> GetPeopleAsync(string token)
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
        (await response.Content.ReadFromJsonAsync<PersonDto>()).Username.ShouldBe("jdoe");
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
}
