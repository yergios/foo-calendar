namespace Api.Data.Models;

public sealed class Person
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public required string DisplayName { get; set; }
    public string HomeCity { get; set; } // For bank holiday matching, e.g. "CH-ZH" ISO 3166-2
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // The username of whoever performed the action. Usually the person themselves, but an
    // admin can create or edit another person's record, so this can't just be Username.
    public required string CreatedBy { get; set; }
    public string UpdatedBy { get; set; }

    public ICollection<Absence> Absences { get; } = [];
}
