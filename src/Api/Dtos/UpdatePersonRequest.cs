using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public sealed record UpdatePersonRequest(
    [MaxLength(100)] string DisplayName,
    [RegularExpression(ValidationPatterns.RegionCode)] string HomeCity, // Format according to ISO 3166-2, e.g. "ES-MD"
    // Where this person's notifications go. Prefilled from the identity provider's email
    // claim when there is one, but editable and stored on the person either way: the claim
    // can be missing, wrong, or absent entirely on a deployment with no identity provider,
    // and an address nobody can correct is an address that quietly stops working.
    //
    // Null means "not supplied, leave it alone" and an empty string means "clear it". The
    // distinction earns its keep because not every caller of this request edits an address:
    // the admin people page sends a name and a region, and an omitted field there must not
    // silently delete someone else's contact details.
    [MaxLength(255)] string Email = null,
    bool EmailOnAbsenceReviewed = false,
    bool EmailOnNewAbsenceRequest = false,
    bool EmailOnWeeklyDigest = false
);
