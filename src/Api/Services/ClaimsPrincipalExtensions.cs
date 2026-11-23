using System.Security.Claims;

namespace Api.Services;

public static class ClaimsPrincipalExtensions
{
    // Every endpoint that records who performed an action reads this same claim, so it's
    // centralized here instead of copied at each call site. Throws rather than returning
    // null, since the callers persist this into a required audit column. Not a domain
    // error: a token the provider signed without the claim means the deployment names the
    // wrong one, which the caller can't fix, so it is a 500 whose logged message tells the
    // operator what to change.
    public static string GetUsername(this ClaimsPrincipal user, string usernameClaimType) =>
        user.FindFirstValue(usernameClaimType)
            ?? throw new InvalidOperationException(
                $"The signed-in user's token has no '{usernameClaimType}' claim, which " +
                "Auth:ClaimTypes:UserName names as the one holding the username. Set " +
                "Auth:ClaimTypes:UserName to the claim the identity provider puts it in.");

    /// <summary>True when any of the named claims carries any of the accepted values.
    /// Claim names are matched exactly, since JWT claim names are case-sensitive, but
    /// values are not: group identifiers in particular are commonly GUIDs whose casing
    /// differs between a provider's dashboard and its tokens.</summary>
    public static bool HasAnyPermission(
        this ClaimsPrincipal user,
        IReadOnlyCollection<string> claimTypes,
        IReadOnlyCollection<string> acceptedValues) =>
        user.Claims.Any(claim =>
            claimTypes.Contains(claim.Type)
            && acceptedValues.Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
}
