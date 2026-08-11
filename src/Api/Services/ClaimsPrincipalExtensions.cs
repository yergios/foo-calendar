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
}
