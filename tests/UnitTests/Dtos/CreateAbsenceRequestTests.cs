using System.ComponentModel.DataAnnotations;

using Api.Data.Models;
using Api.Dtos;

using Shouldly;

namespace UnitTests.Dtos;

public sealed class CreateAbsenceRequestTests
{
    private static List<ValidationResult> Validate(CreateAbsenceRequest request)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true);
        return errors;
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_WellFormedAppointment_HasNoErrors()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20), AbsenceType.Appointment, null,
            new TimeOnly(9, 0), new TimeOnly(10, 0));

        Validate(request).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_NonAppointmentWithStartTimeSet_HasAnError()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 24), AbsenceType.Vacation, null,
            new TimeOnly(9, 0), null);

        Validate(request).ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_AppointmentMissingStartTime_HasAnError()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20), AbsenceType.Appointment, null,
            null, new TimeOnly(10, 0));

        Validate(request).ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_AppointmentEndTimeNotAfterStartTime_HasAnError()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20), AbsenceType.Appointment, null,
            new TimeOnly(10, 0), new TimeOnly(10, 0));

        Validate(request).ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_AppointmentSpanningMoreThanOneDay_HasAnError()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 21), AbsenceType.Appointment, null,
            new TimeOnly(9, 0), new TimeOnly(10, 0));

        Validate(request).ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_WholeDayOther_HasNoErrors()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 24), AbsenceType.Other, null);

        Validate(request).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_WellFormedTimeBasedOther_HasNoErrors()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20), AbsenceType.Other, null,
            new TimeOnly(9, 0), new TimeOnly(10, 0));

        Validate(request).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_OtherWithOnlyStartTimeSupplied_HasAnError()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20), AbsenceType.Other, null,
            new TimeOnly(9, 0), null);

        Validate(request).ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("Spec", "ABS-2")]
    public void Validate_TimeBasedOtherSpanningMoreThanOneDay_HasAnError()
    {
        var request = new CreateAbsenceRequest(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 21), AbsenceType.Other, null,
            new TimeOnly(9, 0), new TimeOnly(10, 0));

        Validate(request).ShouldNotBeEmpty();
    }
}
