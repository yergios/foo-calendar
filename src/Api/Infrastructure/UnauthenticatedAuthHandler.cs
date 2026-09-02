using System.Security.Claims;
using System.Text.Encodings.Web;

using Api.Configuration;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

/// <summary>Authenticates requests without an identity provider, for Auth:Provider=None.
///
/// There is no token and nothing is verified: whoever is reading this deployment is who
/// they say they are. What they say is the identity cookie, set by IdentityEndpoints when
/// someone picks a name; the app is a shared team calendar, so the point of asking at all
/// is that people land on their own row, not that anyone is kept out of anyone else's.
///
/// Two deployments use it. Local development, where the cookie is usually absent and
/// Auth:DevUser stands in so nobody has to pick a name to run the app. And a deliberately
/// unauthenticated deployment on a trusted network, where Auth:AllowUnauthenticatedDeployment
/// has been set: there, an unnamed visitor becomes <see cref="AnonymousUsername"/>, an
/// identity that owns no profile and holds no permissions. That is what puts them in front
/// of the same "set up your profile" form a new hire on an OIDC deployment sees, rather
/// than a screen of failed requests or a second sign-in flow of its own.</summary>
public sealed class UnauthenticatedAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthOptions> authOptions,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Unauthenticated";

    /// <summary>Holds the username the visitor claims to be. Not a session and not a
    /// credential - it is a preference that happens to decide identity, which is exactly
    /// what "unauthenticated" means here.</summary>
    public const string IdentityCookie = "fc_identity";

    /// <summary>Who a visitor is before they have said. Deliberately unusable as a real
    /// username - no person can be created with it and the identity endpoint refuses to
    /// set it - so it can never collide with someone's actual name.</summary>
    public const string AnonymousUsername = "(anonymous)";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = authOptions.Value;
        var claimTypes = auth.ClaimTypes;

        var username = Request.Cookies[IdentityCookie];
        var named = !string.IsNullOrWhiteSpace(username);

        // Development stands in the dev user so nobody has to name themselves to run the
        // app locally. A real deployment must not: every unnamed visitor would land on one
        // shared calendar row. They get the anonymous identity instead, which owns nothing.
        if (!named)
            username = environment.IsDevelopment() ? auth.DevUser.Username : AnonymousUsername;

        var claims = new List<Claim> { new(claimTypes.UserName, username) };

        // Only ever read when a person is being created for the first time; an identity
        // that already exists is read from the database by its username. In development
        // that means the dev user's own details; for a picked identity it means the
        // onboarding form fills these in instead.
        if (username == auth.DevUser.Username)
        {
            claims.Add(new Claim(claimTypes.FirstName, auth.DevUser.FirstName));
            claims.Add(new Claim(claimTypes.LastName, auth.DevUser.LastName));
            claims.Add(new Claim(claimTypes.Email, auth.DevUser.Email));
        }

        // Under the first configured claim name; the policies accept any of them, so one is
        // enough to stand in for whatever a real provider would have emitted. Everyone who
        // has named themselves gets the same set: with no identity provider there is nothing
        // to check a person's permissions against, and anyone who wanted more could simply
        // claim another name. Auth:DevUser:GrantAllPermissions still narrows it, which is a
        // way to run the whole deployment as plain members, not a way to keep one visitor
        // out of the admin pages.
        //
        // A visitor who hasn't named themselves holds nothing, so the interface offers them
        // the profile form and not the admin area. That is tidiness, not a boundary: naming
        // yourself is one form away.
        if (named || environment.IsDevelopment())
        {
            var permissionClaim = claimTypes.Permissions.FirstOrDefault() ?? "permissions";
            claims.AddRange(
                auth.EffectiveDevPermissions().Select(p => new Claim(permissionClaim, p)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
