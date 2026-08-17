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
    List<string> ColorPalette,
    bool AbsenceApprovalsEnabled,
    // Whether the deployment can send email at all, i.e. Email:Host is configured. Same
    // reasoning as AbsenceApprovalsEnabled: the SPA uses this to hide the opt-in checkboxes
    // rather than showing toggles that would save fine but never actually do anything.
    bool EmailEnabled,
    // The raw IANA id, e.g. "Europe/Zurich". Always set: Team:HomeTimeZone is required.
    // Printed verbatim next to a time-based absence's time inputs, no formatting.
    string HomeTimeZone);

/// <summary>Everything the SPA needs before anyone has signed in, including how to sign in
/// at all. Fetched once at startup so branding and IdP settings are deployment config
/// rather than values compiled into the bundle.</summary>
public sealed record BootstrapConfigResponse(
    string AppName,
    string SupportUrl,
    string IssueUrl,
    BootstrapAuthConfig Auth);

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
    BootstrapDevUser DevUser);

/// <summary>Only populated when Auth:Provider=None, which is Development-only. It lets the
/// SPA present the same identity the API's DevAuthHandler is already authenticating as.</summary>
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
        IOptions<PersonColorPaletteOptions> palette,
        IOptions<AbsenceApprovalOptions> absenceApprovals,
        IOptions<EmailOptions> email) =>
        Results.Ok(new AppConfigResponse(
            team.Value.HomeRegion,
            palette.Value,
            absenceApprovals.Value.Enabled,
            !string.IsNullOrWhiteSpace(email.Value.Host),
            team.Value.HomeTimeZone));

    private static IResult GetBootstrapConfig(
        HttpContext http,
        IOptions<BrandingOptions> branding,
        IOptions<AuthOptions> authOptions)
    {
        var auth = authOptions.Value;

        // This only changes on a redeploy, but a browser holding a stale copy would keep
        // pointing at the old IdP with no way for the user to notice, so it is never
        // stored. It is one small request, once per page load, before anything else.
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new BootstrapConfigResponse(
            branding.Value.AppName,
            branding.Value.SupportUrl,
            branding.Value.IssueUrl,
            new BootstrapAuthConfig(
                auth.Provider,
                auth.Authority,
                auth.ClientId,
                auth.Audience,
                auth.Scope,
                auth.RedirectUri,
                auth.Permissions,
                auth.ClaimTypes.Permissions,
                auth.Provider == AuthOptions.ProviderNone
                    ? new BootstrapDevUser(
                        auth.DevUser.FirstName,
                        auth.DevUser.LastName,
                        auth.DevUser.Email,
                        // Resolved rather than copied, so the SPA holds exactly what
                        // DevAuthHandler is authenticating the requests as.
                        auth.EffectiveDevPermissions())
                    : null)));
    }
}
