namespace Api.Configuration;

/// <summary>
/// The things a person can be granted beyond being a member of the team. Members
/// need none of these: signing in is what lets you keep your own profile, your own
/// absences, and the team's manually added bank holidays, which everyone shares
/// responsibility for.
///
/// Each name is both the configuration key under Auth:Permissions and the authorization
/// policy an endpoint asks for, so there is one spelling of a permission and no mapping
/// table between config and code. Which claim values grant one is entirely a deployment's
/// decision, see <see cref="AuthOptions.Permissions"/>.
/// </summary>
public static class Permissions
{
    /// <summary>Create, edit, delete, and import profiles for anyone.</summary>
    public const string PeopleManage = "PeopleManage";

    /// <summary>Read everyone's absence totals side by side.</summary>
    public const string ReportsRead = "ReportsRead";

    /// <summary>Trigger a bank-holiday sync by hand. Separate from managing holidays, which
    /// anyone can do: this one calls out to the holiday source and writes in bulk.</summary>
    public const string HolidaysSync = "HolidaysSync";

    /// <summary>Read the audit log, which records who changed or deleted what.</summary>
    public const string AuditRead = "AuditRead";

    /// <summary>Every permission there is, in the order the admin page presents them.
    /// Program.cs registers one policy per entry, and the configuration must name them all.
    /// </summary>
    public static readonly string[] All = [PeopleManage, ReportsRead, HolidaysSync, AuditRead];
}
