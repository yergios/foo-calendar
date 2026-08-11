using System.ComponentModel.DataAnnotations;

using Api.Data.Models;

namespace Api.Dtos;

public sealed record CreateAbsenceRequest(
    [Required] DateOnly StartDate,
    [Required] DateOnly EndDate,
    [Required] AbsenceType Type,
    [MaxLength(500)] string Note
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EndDate < StartDate)
            yield return new ValidationResult(
                "EndDate cannot be before StartDate.",
                [nameof(EndDate)]);
    }
}
