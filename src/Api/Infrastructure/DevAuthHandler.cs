using System.Security.Claims;
using System.Text.Encodings.Web;

using Api.Configuration;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

/// <summary>Dev-only auth bypass: authenticates every request as the fixed Auth:DevUser
/// identity, no token or IdP involved. Wired up only when Auth:Provider=None, which
/// Program.cs restricts to the Development environment.</summary>
public sealed class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = authOptions.Value;
        var claimTypes = auth.ClaimTypes;

        var claims = new List<Claim>
        {
            new(claimTypes.UserName, auth.DevUser.Username),
            new(claimTypes.FirstName, auth.DevUser.FirstName),
            new(claimTypes.LastName, auth.DevUser.LastName),
            new(claimTypes.Email, auth.DevUser.Email),
        };
        // Under the first configured claim name; the policy accepts any of them, so one is
        // enough to stand in for whatever a real provider would have emitted.
        var permissionClaim = claimTypes.Permissions.FirstOrDefault() ?? "permissions";
        claims.AddRange(auth.DevUser.Permissions.Select(p => new Claim(permissionClaim, p)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
