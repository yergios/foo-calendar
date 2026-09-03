using System.ComponentModel.DataAnnotations;

using Api.Configuration;

namespace Api.Dtos;

// Attributes are aimed at the generated properties rather than the constructor
// parameters, because this request is validated in two places: the endpoint, via the
// minimal API's validation, and the CSV import, via Validator.TryValidateObject, which
// only reads property attributes. Aiming them here keeps one set of rules for both.
public sealed record CreatePersonRequest(
    [property: MaxLength(100)] string Username,
    [property: MaxLength(100)] string FirstName,
    [property: MaxLength(100)] string LastName,
    [property: MaxLength(100)] string DisplayName,
    [property: RegularExpression(ValidationPatterns.RegionCode)] string HomeCity, // Format according to ISO 3166-2, e.g. "ES-MD"
    [property: MaxLength(255)] string Email = null,
    bool EmailOnAbsenceReviewed = false,
    bool EmailOnNewAbsenceRequest = false,
    bool EmailOnWeeklyDigest = false
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // Here rather than at each endpoint because both remaining ways to create a person
        // run this record's rules: the admin create binds it from the body, and the CSV
        // import calls Validator.TryValidateObject on it. Claiming this name would put every
        // visitor who hasn't named themselves onto that person's row on an unauthenticated
        // deployment. (PeopleEndpoints.CreateMe checks it separately: it builds this record
        // in code from the identity cookie, so nothing validates it on the way through.)
        if (Username == AuthOptions.AnonymousUsername)
            yield return new ValidationResult(
                $"\"{AuthOptions.AnonymousUsername}\" is reserved and cannot be a username.",
                [nameof(Username)]);
    }
}
