using Api.Infrastructure;

namespace Api.Endpoints;

public sealed record ChooseIdentityRequest(string Username);

/// <summary>Identity for deployments with no identity provider (Auth:Provider=None): the
/// visitor says who they are and the app takes their word for it.
///
/// There is no endpoint here that reads anything back. Claiming a name is the whole
/// interaction, and it happens inside the same "set up your profile" form every new person
/// fills in - so the app never needs a roster of who it could be, and never hands one to
/// someone who hasn't said who they are. Whether the claimed name already has a profile is
/// answered by GET /api/people/me afterwards, which is authenticated by the very cookie
/// this sets.
///
/// Mapped only in that mode, so on an OIDC deployment these routes do not exist at all
/// rather than existing and refusing: there is no configuration in which both an IdP and a
/// self-asserted name are accepted.</summary>
public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/identity").AllowAnonymous().WithTags("Identity");

        group.MapPost("", ChooseIdentity);
        group.MapDelete("", ForgetIdentity);
    }

    private static IResult ChooseIdentity(HttpContext http, ChooseIdentityRequest request)
    {
        var username = request?.Username?.Trim();

        if (string.IsNullOrEmpty(username))
            return Results.BadRequest("A username is required.");

        // The name the handler gives a visitor who hasn't chosen one. Claiming it would put
        // them on the same row as everyone who hasn't chosen either.
        if (username == UnauthenticatedAuthHandler.AnonymousUsername)
            return Results.BadRequest("That username is reserved.");

        // Not checked against the people table on purpose: a name nobody has yet is how the
        // first person on a fresh deployment gets one, and a name that already exists is how
        // someone who cleared their cookie gets back to their own profile. Both are the same
        // request, and which one happened is something the caller finds out next.
        http.Response.Cookies.Append(
            UnauthenticatedAuthHandler.IdentityCookie, username, CookieOptions(http));

        return Results.NoContent();
    }

    private static IResult ForgetIdentity(HttpContext http)
    {
        http.Response.Cookies.Delete(
            UnauthenticatedAuthHandler.IdentityCookie, CookieOptions(http));

        return Results.NoContent();
    }

    private static CookieOptions CookieOptions(HttpContext http) => new()
    {
        // HttpOnly even though this is not a credential: nothing in the SPA reads it back,
        // so there is no reason for script to reach it.
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        // Only over HTTPS when the request itself came over HTTPS: this mode's whole point
        // is deployments on internal networks, which are commonly plain HTTP, and a Secure
        // cookie there would simply never be stored.
        Secure = http.Request.IsHttps,
        // Scoped to the sub-path the app is served under, so two deployments behind one
        // hostname don't overwrite each other's identity.
        Path = http.Request.PathBase.HasValue ? http.Request.PathBase.Value : "/",
        // A year: this is a convenience, and being asked your own name every week on a
        // calendar you keep open all day is worse than the cookie lasting.
        MaxAge = TimeSpan.FromDays(365),
    };
}
