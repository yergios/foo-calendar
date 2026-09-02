using Api.Data.Models;

namespace Api.Dtos;

public sealed record PersonDto(
    int Id,
    string Username,
    string DisplayName,
    string HomeCity,
    // Someone's own address, so it rides on includePreferences with the notification
    // toggles rather than going out to the colleagues list.
    string Email,
    bool EmailOnAbsenceReviewed,
    bool EmailOnNewAbsenceRequest,
    bool EmailOnWeeklyDigest)
{
    /// <summary>An address and its notification preferences are only ever meaningful to
    /// the person they belong to, so <paramref name="includePreferences"/> is false for the
    /// colleagues list and true for the /me responses. Deliberately has no default, so the
    /// decision is made at every call site rather than inherited from one.</summary>
    public static PersonDto FromEntity(Person person, bool includePreferences) => new(
        person.Id,
        person.Username,
        person.DisplayName,
        person.HomeCity,
        includePreferences ? person.Email : null,
        includePreferences && person.EmailOnAbsenceReviewed,
        includePreferences && person.EmailOnNewAbsenceRequest,
        includePreferences && person.EmailOnWeeklyDigest);
}
