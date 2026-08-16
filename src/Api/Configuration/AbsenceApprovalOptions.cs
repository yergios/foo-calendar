namespace Api.Configuration;

/// <summary>Whether an absence request needs a review before it counts. Off by default, so
/// a new deployment gets today's behavior: every absence is approved the instant it's
/// created. Turning this on doesn't require every deployment that never asked for it to
/// configure Permissions.AbsencesApprove, since that permission is optional, see
/// <see cref="Permissions.Optional"/>.</summary>
public sealed class AbsenceApprovalOptions
{
    public const string SectionName = "AbsenceApprovals";

    public bool Enabled { get; init; }
}
