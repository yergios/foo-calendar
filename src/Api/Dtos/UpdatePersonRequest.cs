using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public sealed record UpdatePersonRequest(
    [MaxLength(100)] string DisplayName,
    [RegularExpression(ValidationPatterns.RegionCode)] string HomeCity, // Format according to ISO 3166-2, e.g. "ES-MD"
    [Required, RegularExpression(ValidationPatterns.HexColor)] string Color,    // Hex color format, e.g. "#EC4899"
    bool EmailOnAbsenceReviewed = false,
    bool EmailOnNewAbsenceRequest = false,
    bool EmailOnWeeklyDigest = false
);
