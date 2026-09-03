using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public sealed record UpdatePersonRequest(
    [MaxLength(100)] string DisplayName,
    [RegularExpression(ValidationPatterns.RegionCode)] string HomeCity // Format according to ISO 3166-2, e.g. "ES-MD"
);
