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
)
{
    /// <summary>Whether this request has anything to say about the address at all. False for
    /// the admin people page, which sends a name and a region.</summary>
    public bool EditsEmail => Email is not null;

    /// <summary>The address this request means, with an emptied box and "never had one"
    /// collapsed to the same answer: none. Read alongside <see cref="EditsEmail"/>, which is
    /// what separates "no address" from "not my business".</summary>
    public string ResolvedEmail => string.IsNullOrWhiteSpace(Email) ? null : Email;
}
