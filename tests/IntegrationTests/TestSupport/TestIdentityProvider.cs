using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IntegrationTests.TestSupport;

/// <summary>
/// Stands in for the OIDC provider a deployment trusts: a signing key of its own, and the
/// metadata the app would otherwise download from <see cref="Issuer"/>. Handed to
/// <see cref="TestApp.IdentityProvider"/>, the app validates bearer tokens against this
/// metadata exactly as it would against a real provider's, with nothing sent over the
/// network. <see cref="Mint"/> issues the tokens.
/// </summary>
public sealed class TestIdentityProvider : IDisposable
{
    public const string Issuer = "https://idp.example.test/";
    public const string Audience = "foo-calendar";
    public const string ClientId = "foo-calendar-spa";

    private readonly RSA rsa = RSA.Create(2048);

    public TestIdentityProvider()
    {
        SigningKey = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        Configuration.SigningKeys.Add(SigningKey);
    }

    public SecurityKey SigningKey { get; }

    /// <summary>What the provider's discovery document and key set would say.</summary>
    public OpenIdConnectConfiguration Configuration { get; }

    /// <summary>A signed access token. Each argument left out is what this provider issues
    /// for the app: its own issuer, the app's audience, valid for the next hour, signed
    /// with its own key.</summary>
    public string Mint(
        IEnumerable<Claim> claims = null,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey signingKey = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims ?? [new Claim("sub", "token-user")]),
            IssuedAt = expiry.AddHours(-2),
            NotBefore = expiry.AddHours(-2),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(
                signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public void Dispose() => rsa.Dispose();
}
