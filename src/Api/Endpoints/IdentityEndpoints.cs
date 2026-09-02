using Api.Data;
using Api.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

/// <summary>One name a visitor can claim on an unauthenticated deployment.</summary>
public sealed record IdentityOption(string Username, string DisplayName);

/// <summary>Who the visitor currently is, and who else they could be.</summary>
public sealed record IdentityStateResponse(string Current, List<IdentityOption> People);

public sealed record ChooseIdentityRequest(string Username);

/// <summary>Identity for deployments with no identity provider (Auth:Provider=None): the
/// visitor says who they are and the app takes their word for it.
///
/// Mapped only in that mode, so on an OIDC deployment these routes do not exist at all
/// rather than existing and refusing - there is no configuration in which both an IdP and
/// a self-asserted name are accepted.
///
/// Anonymous by necessity: this is what runs before anyone has an identity. It does hand
/// the roster of names to whoever can reach the app, which is not a leak in the setting it
/// is meant for - a deployment with no authentication is already showing that visitor the
/// whole calendar - but it is the reason this mode is opt-in and warned about.</summary>
public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/identity").AllowAnonymous().WithTags("Identity");

        group.MapGet("", GetIdentity);
        group.MapPost("", ChooseIdentity);
        group.MapDelete("", ForgetIdentity);
    }

    private static async Task<IResult> GetIdentity(HttpContext http, AppDbContext db)
    {
        var people = await db.People
            .OrderBy(p => p.DisplayName)
            .Select(p => new IdentityOption(p.Username, p.DisplayName))
            .ToListAsync(http.RequestAborted);

        return Results.Ok(new IdentityStateResponse(
            http.Request.Cookies[UnauthenticatedAuthHandler.IdentityCookie],
            people));
    }

    private static IResult ChooseIdentity(HttpContext http, ChooseIdentityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Username))
            return Results.BadRequest("A username is required.");

        // Not checked against the people table on purpose: choosing a name nobody has yet
        // is how the first person on a fresh deployment gets one, and the onboarding form
        // creates the profile immediately afterwards.
        http.Response.Cookies.Append(
            UnauthenticatedAuthHandler.IdentityCookie, request.Username, CookieOptions(http));

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
        // HttpOnly even though this is not a credential: nothing in the SPA reads it back
        // (GET above reports it instead), so there is no reason for script to reach it.
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
