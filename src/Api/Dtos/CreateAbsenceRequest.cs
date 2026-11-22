using System.ComponentModel.DataAnnotations;

using Api.Data.Models;

namespace Api.Dtos;

public sealed record CreateAbsenceRequest(
    [Required] DateOnly StartDate,
    [Required] DateOnly EndDate,
    [Required] AbsenceType Type,
    [MaxLength(500)] string Note,
    // Not [Required] here since they're legitimately null for a whole-day absence.
    // Whether they're required, forbidden, or optional depends on Type (and, for Other, on
    // whether the caller supplied one), so that's enforced in Validate() below instead.
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
            yield return new ValidationResult(
                "EndDate cannot be before StartDate.",
                [nameof(EndDate)]);

        // Appointment is always time-based. Other is whole-day by default but may become
        // time-based by supplying either time field; Vacation is never time-based,
        // whole-day absences only.
        var suppliedTime = StartTime is not null || EndTime is not null;
        var isTimeBased = Type == AbsenceType.Appointment || (Type == AbsenceType.Other && suppliedTime);

        if (isTimeBased)
        {
            if (StartDate != EndDate)
                yield return new ValidationResult(
                    "A time-based absence is a single day: StartDate and EndDate must match.",
                    [nameof(EndDate)]);

            if (StartTime is null || EndTime is null)
                yield return new ValidationResult(
                    "StartTime and EndTime are required for a time-based absence.",
                    [nameof(StartTime), nameof(EndTime)]);
            else if (EndTime <= StartTime)
                yield return new ValidationResult(
                    "EndTime must be after StartTime.",
                    [nameof(EndTime)]);
        }
        else if (suppliedTime)
        {
            yield return new ValidationResult(
                "StartTime and EndTime only apply to Appointment or a time-based Other absence.",
                [nameof(StartTime), nameof(EndTime)]);
        }
    }
}
