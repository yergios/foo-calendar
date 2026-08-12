using System.ComponentModel.DataAnnotations;

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
    [property: Required, RegularExpression(ValidationPatterns.HexColor)] string Color,    // Hex color format, e.g. "#EC4899"
    [property: MaxLength(255)] string Email = null
);
