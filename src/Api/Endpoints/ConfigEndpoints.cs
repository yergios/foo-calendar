using Api.Configuration;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public sealed record AppConfigResponse(List<CountryRegions> Regions, List<string> ColorPalette);

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
    string AdminPermission,
    string PermissionsClaim,
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
        app.MapGet("/api/config", GetConfig).RequireAuthorization();
        app.MapGet("/api/config/bootstrap", GetBootstrapConfig).AllowAnonymous();
    }

    private static IResult GetConfig(
        IOptions<RegionCatalogOptions> regions,
        IOptions<PersonColorPaletteOptions> palette) =>
        Results.Ok(new AppConfigResponse(regions.Value, palette.Value));

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
                auth.AdminPermission,
                auth.ClaimTypes.Permissions,
                auth.Provider == AuthOptions.ProviderNone
                    ? new BootstrapDevUser(
                        auth.DevUser.FirstName,
                        auth.DevUser.LastName,
                        auth.DevUser.Email,
                        auth.DevUser.Permissions)
                    : null)));
    }
}
