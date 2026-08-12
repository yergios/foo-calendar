namespace Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Value of <see cref="Provider"/> that validates real bearer tokens.</summary>
    public const string ProviderOidc = "Oidc";

    /// <summary>Value of <see cref="Provider"/> that turns on the dev-only bypass.</summary>
    public const string ProviderNone = "None";

    /// <summary>Authorization policy gating the admin area. Registered in Program.cs from
    /// <see cref="AdminPermissions"/> and <see cref="ClaimTypeOptions.Permissions"/>.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>"Oidc" validates bearer tokens against Authority/Audience. "None" is a
    /// dev-only bypass (see DevAuthHandler) that skips SSO entirely.</summary>
    public string Provider { get; init; } = ProviderOidc;

    public string Authority { get; init; } = "";
    public string Audience { get; init; } = "";

    /// <summary>Claim values that grant access to the admin area. Holding any one of them
    /// is enough, which is what lets a deployment accept, say, both an app role and a
    /// group id without the identity provider having to mint a single tidy permission.
    /// </summary>
    public List<string> AdminPermissions { get; init; } = [];

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

    /// <summary>Drops blank list entries, which come from shortening a list through
    /// environment variables: the leftover indexes are set to empty rather than removed.
    /// An empty claim name would otherwise match claims that have no type, and an empty
    /// accepted value would match a claim with no value.</summary>
    public void RemoveBlankEntries()
    {
        AdminPermissions.RemoveAll(string.IsNullOrWhiteSpace);
        ClaimTypes.Permissions.RemoveAll(string.IsNullOrWhiteSpace);
    }
}

/// <summary>Claim names to read identity/permission info from. Defaults are standard OIDC
/// claims, but "Permissions" in particular varies by IdP and commonly needs overriding.</summary>
public sealed class ClaimTypeOptions
{
    public string UserName { get; init; } = "sub";
    public string FirstName { get; init; } = "given_name";
    public string LastName { get; init; } = "family_name";
    public string Email { get; init; } = "email";

    /// <summary>Claims that carry permissions, searched in order. More than one because
    /// providers split them up: Entra ID puts app roles in "roles" and group ids in
    /// "groups", and a deployment may want to accept either.
    ///
    /// Deliberately empty rather than defaulting to ["permissions"]: configuration binds
    /// into an existing list by appending, so a default here would stay accepted even
    /// after a deployment configured its own claim names. The shipped default lives in
    /// appsettings.json, where overriding it actually replaces it.</summary>
    public List<string> Permissions { get; init; } = [];
}

public sealed class DevUserOptions
{
    public string Username { get; init; } = "dev-user";
    public string FirstName { get; init; } = "Dev";
    public string LastName { get; init; } = "User";
    public string Email { get; init; } = "dev@example.local";
    public List<string> Permissions { get; init; } = [];
}
