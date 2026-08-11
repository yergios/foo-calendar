namespace Api.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>"Oidc" validates bearer tokens against Authority/Audience. "None" is a
    /// dev-only bypass (see DevAuthHandler) that skips SSO entirely.</summary>
    public string Provider { get; init; } = "Oidc";

    public string Authority { get; init; } = "";
    public string Audience { get; init; } = "";
    public required string AdminPermission { get; init; }

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
