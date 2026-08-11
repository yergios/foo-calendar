using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public sealed record AddManualHolidayRequest(
    [Required, RegularExpression(ValidationPatterns.RegionCode)] string Region, // Format according to ISO 3166-2, e.g. "ES-MD"
    [Required] DateOnly Date,
    [Required, MaxLength(200)] string Name,
    bool IsRecurringAnnually = false
);
