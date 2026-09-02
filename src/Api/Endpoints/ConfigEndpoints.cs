using Api.Configuration;
using Api.Services;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

/// <summary>Deployment-level settings the SPA needs after signing in. Everything here is
/// fixed until a redeploy, which is what lets the client cache it outright. Which regions
/// the team occupies deliberately isn't here: that changes as people come and go, so it is
/// derived from the people list instead.</summary>
public sealed record AppConfigResponse(
    string HomeRegion,
    bool AbsenceApprovalsEnabled,
    // Whether the deployment can send email at all, i.e. Email:Host is configured. Same
    // reasoning as AbsenceApprovalsEnabled: the SPA uses this to hide the opt-in checkboxes
    // rather than showing toggles that would save fine but never actually do anything.
    bool EmailEnabled,
    // The raw IANA id, e.g. "Europe/Zurich". Always set: Team:HomeTimeZone is required.
    // Printed verbatim next to a time-based absence's time inputs, no formatting.
    string HomeTimeZone);

/// <summary>Who this deployment is and how to sign in to it. Inlined into the SPA shell
/// (see SpaShell) rather than fetched, so reading it costs no round trip and no await -
/// which is what decides what belongs here: deployment identity that is fixed until a
/// redeploy and that the app needs *synchronously*, before it has authenticated anyone.
///
/// The /401 and /500 routes sit outside the authenticated layout and have to render when
/// signing in is the thing that failed, so their two links qualify. The header's team name
/// qualifies because reading it asynchronously would flash the wrong name first. What
/// shapes the signed-in app - approvals, email, regions, the timezone - does not: that is
/// AppConfigResponse above. The product's own name is in neither, being a constant.</summary>
public sealed record BootstrapConfigResponse(
    // The deploying team's own name for the header. Empty falls back to the product's
    // name, so the header always has something to link home with.
    string OrgName,
    string SupportUrl,
    string IssueUrl,
    BootstrapAuthConfig Auth);

/// <summary>The IdP half of the bootstrap payload.</summary>
public sealed record BootstrapAuthConfig(
    string Provider,
    string Authority,
    string ClientId,
    string Audience,
    string Scope,
    string RedirectUri,
    // Which claim values grant each permission, so the SPA can hide what the API would
    // refuse anyway. The API enforces the same map independently: this is for the
    // interface, not for the gate.
    Dictionary<string, List<string>> Permissions,
    List<string> PermissionsClaims,
    // True only for a real deployment running with authentication off, never for local
    // development, which is also Provider=None but is nobody's security problem. The SPA
    // shows a standing banner on the strength of this: a deployment that has quietly lost
    // its identity provider should be obvious to the people using it, not just to whoever
    // reads the startup log.
    bool UnauthenticatedDeployment,
    BootstrapDevUser DevUser);

/// <summary>Only populated when Auth:Provider=None. It lets the SPA present the same
/// identity the API's UnauthenticatedAuthHandler is already authenticating as.</summary>
public sealed record BootstrapDevUser(
    string FirstName,
    string LastName,
    string Email,
    List<string> Permissions);

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config", GetConfig).RequireAuthorization().WithTags("Config");
        app.MapGet("/api/config/bootstrap", GetBootstrapConfig).AllowAnonymous().WithTags("Config");
    }

    private static IResult GetConfig(
        IOptions<TeamOptions> team,
        IOptions<AbsenceApprovalOptions> absenceApprovals,
        IOptions<EmailOptions> email) =>
        Results.Ok(new AppConfigResponse(
            team.Value.HomeRegion,
            absenceApprovals.Value.Enabled,
            !string.IsNullOrWhiteSpace(email.Value.Host),
            team.Value.HomeTimeZone));

    private static IResult GetBootstrapConfig(
        HttpContext http,
        IHostEnvironment environment,
        IOptions<TeamOptions> team,
        IOptions<AuthOptions> authOptions)
    {
        // In a served deployment the SPA reads this straight out of its own shell and never
        // gets here; this endpoint is what the Vite dev server's SPA falls back to, and a
        // place to look when a deployment's auth config is in question. Either way a
        // browser holding a stale copy would keep pointing at the old IdP with no way for
        // the user to notice, so it is never stored.
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(Build(team.Value, authOptions.Value, environment));
    }

    /// <summary>The payload itself, so the SPA shell can embed exactly what this endpoint
    /// would have returned rather than a second, drifting copy of the same mapping.</summary>
    internal static BootstrapConfigResponse Build(
        TeamOptions team, AuthOptions auth, IHostEnvironment environment) =>
        new(team.OrgName,
            team.SupportUrl,
            team.IssueUrl,
            BuildAuth(auth, environment));

    private static BootstrapAuthConfig BuildAuth(AuthOptions auth, IHostEnvironment environment) =>
        new(auth.Provider,
            auth.Authority,
            auth.ClientId,
            auth.Audience,
            auth.Scope,
            auth.RedirectUri,
            auth.Permissions,
            auth.ClaimTypes.Permissions,
            auth.Provider == AuthOptions.ProviderNone && !environment.IsDevelopment(),
            auth.Provider == AuthOptions.ProviderNone
                ? new BootstrapDevUser(
                    auth.DevUser.FirstName,
                    auth.DevUser.LastName,
                    auth.DevUser.Email,
                    // Resolved rather than copied, so the SPA holds exactly what
                    // the unauthenticated handler is authenticating the requests as.
                    auth.EffectiveDevPermissions())
                : null);
}
