namespace Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Value of <see cref="Provider"/> that validates real bearer tokens.</summary>
    public const string ProviderOidc = "Oidc";

    /// <summary>Value of <see cref="Provider"/> that turns authentication off.</summary>
    public const string ProviderNone = "None";

    /// <summary>"Oidc" validates bearer tokens against Authority/Audience. "None" skips
    /// authentication entirely (see UnauthenticatedAuthHandler): visitors say who they are
    /// and the app believes them. Outside Development that also needs
    /// <see cref="AllowUnauthenticatedDeployment"/>.</summary>
    public string Provider { get; init; } = ProviderOidc;

    /// <summary>Permits Provider=None outside Development, for a team on a network where
    /// reaching the app is already the access control - a private LAN, a VPN, a Tailscale
    /// tailnet - and standing up an identity provider would be the only thing standing
    /// between them and a shared calendar.
    ///
    /// A second switch rather than Provider=None alone, because the two say different
    /// things: one is "no IdP configured", which is also what a half-finished deployment
    /// looks like, and this one is "I know what that means here". What it means is that
    /// anyone who can reach the app is anyone they choose to be, including whoever holds
    /// the permissions, and that the roster of names is readable before signing in. On a
    /// network where that is not true of everything else already, it is the wrong
    /// setting.</summary>
    public bool AllowUnauthenticatedDeployment { get; init; }

    public string Authority { get; init; } = "";
    public string Audience { get; init; } = "";

    /// <summary>Which claim values grant each permission in <see cref="Api.Configuration.Permissions.All"/>,
    /// keyed by permission name. Holding any one of a permission's values is enough, which
    /// is what lets a deployment accept, say, both an app role and a group id without the
    /// identity provider having to mint a single tidy permission. Nothing stops two
    /// permissions from naming the same value: that is how a deployment that wants one
    /// undivided admin role configures one.
    /// </summary>
    public Dictionary<string, List<string>> Permissions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>The claim values granting one permission, or nothing at all when the
    /// deployment never configured it. Never null, so a caller can't accidentally treat
    /// "not configured" as "granted".</summary>
    public IReadOnlyList<string> ValuesFor(string permission) =>
        Permissions.TryGetValue(permission, out var values) ? values : [];

    /// <summary>Every claim value that grants anything, deduplicated. What the dev identity
    /// carries when it is set up to hold everything, see <see cref="DevUserOptions"/>.
    /// </summary>
    public List<string> AllPermissionValues() =>
        [.. Permissions.Values.SelectMany(values => values).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>What an unauthenticated visitor actually holds. Resolved here rather than
    /// at each use so the API's UnauthenticatedAuthHandler and the SPA's bootstrap config
    /// can't disagree about it.</summary>
    public List<string> EffectiveDevPermissions() =>
        DevUser.GrantAllPermissions ? AllPermissionValues() : DevUser.Permissions;

    /// <summary>Drops blank list entries, which come from shortening a list through
    /// environment variables: the leftover indexes are set to empty rather than removed.
    /// An empty claim name would otherwise match claims that have no type, and an empty
    /// accepted value would match a claim with no value.</summary>
    public void RemoveBlankEntries()
    {
        foreach (var values in Permissions.Values)
            values.RemoveAll(string.IsNullOrWhiteSpace);

        ClaimTypes.Permissions.RemoveAll(string.IsNullOrWhiteSpace);
        DevUser.Permissions.RemoveAll(string.IsNullOrWhiteSpace);
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

    /// <summary>Grants the dev identity every value listed under Auth:Permissions, so local
    /// development has the whole app available without anyone maintaining a list that
    /// drifts as permissions are added. Set false to try the app as a plain member, or as
    /// whatever <see cref="Permissions"/> names.</summary>
    public bool GrantAllPermissions { get; init; } = true;

    /// <summary>Claim values the dev identity carries when
    /// <see cref="GrantAllPermissions"/> is off. Ignored while it is on.</summary>
    public List<string> Permissions { get; init; } = [];
}
