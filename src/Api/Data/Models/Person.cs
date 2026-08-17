namespace Api.Data.Models;

public sealed class Person
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public required string DisplayName { get; set; }
    public required string Color { get; set; }
    public string HomeCity { get; set; } // For bank holiday matching, e.g. "CH-ZH" ISO 3166-2
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // The username of whoever performed the action. Usually the person themselves, but an
    // admin can create or edit another person's record, so this can't just be Username.
    public required string CreatedBy { get; set; }
    public string UpdatedBy { get; set; }

    // Opt-in email preferences, off by default. EmailOnNewAbsenceRequest is only ever
    // acted on for whoever has it set, with no re-check against AbsencesApprove: it's a
    // stored preference, not a live permission lookup, so it can lag a revoked permission
    // until the person next saves their profile.
    public bool EmailOnAbsenceReviewed { get; set; }
    public bool EmailOnNewAbsenceRequest { get; set; }
    public bool EmailOnWeeklyDigest { get; set; }

    public ICollection<Absence> Absences { get; set; } = [];
}
