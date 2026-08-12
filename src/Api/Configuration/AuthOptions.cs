namespace Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Value of <see cref="Provider"/> that turns on the dev-only bypass.</summary>
    public const string ProviderNone = "None";

    /// <summary>"Oidc" validates bearer tokens against Authority/Audience. "None" is a
    /// dev-only bypass (see DevAuthHandler) that skips SSO entirely.</summary>
    public string Provider { get; init; } = "Oidc";

    public string Authority { get; init; } = "";
    public string Audience { get; init; } = "";
    public required string AdminPermission { get; init; }

    /// <summary>The SPA's own OIDC client id. Not used by the API itself, it is handed to
    /// the browser at runtime so the frontend doesn't have to be rebuilt per IdP.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>Scopes the SPA requests. Frontend-only, like <see cref="ClientId"/>.</summary>
    public string Scope { get; init; } = "openid profile email offline_access";

    /// <summary>Where the IdP sends the browser back after login. Relative by default, so
    /// the same deployment works on any hostname; the SPA resolves it against its own
    /// origin. An absolute URL is passed through untouched.</summary>
    public string RedirectUri { get; init; } = "/callback";

    public ClaimTypeOptions ClaimTypes { get; init; } = new();
    public DevUserOptions DevUser { get; init; } = new();
}

/// <summary>Claim names to read identity/permission info from. Defaults are standard OIDC
/// claims, but "Permissions" in particular varies by IdP and commonly needs overriding.</summary>
public sealed class ClaimTypeOptions
{
    public string UserName { get; init; } = "sub";
    public string FirstName { get; init; } = "given_name";
    public string LastName { get; init; } = "family_name";
    public string Email { get; init; } = "email";
    public string Permissions { get; init; } = "permissions";
}

public sealed class DevUserOptions
{
    public string Username { get; init; } = "dev-user";
    public string FirstName { get; init; } = "Dev";
    public string LastName { get; init; } = "User";
    public string Email { get; init; } = "dev@example.local";
    public List<string> Permissions { get; init; } = [];
}
