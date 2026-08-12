using System.Security.Claims;

namespace Api.Services;

public static class ClaimsPrincipalExtensions
{
    // Every endpoint that records who performed an action reads this same claim, so it's
    // centralized here instead of copied at each call site. Throws rather than returning
    // null, since the callers persist this into a required audit column.
    public static string GetUsername(this ClaimsPrincipal user, string usernameClaimType) =>
        user.FindFirstValue(usernameClaimType)
            ?? throw new InvalidOperationException("No Username identifier claim present.");

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
