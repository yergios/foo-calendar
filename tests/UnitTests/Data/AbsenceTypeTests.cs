using Api.Data.Models;

using Shouldly;

namespace UnitTests.Data;

public sealed class AbsenceTypeTests
{
    // A type is visible to the whole team, so one naming a health reason would publish
    // health data (docs/decisions/0007). Adding a type is meant to fail here and be decided.
    [Fact]
    [Trait("Spec", "ABS-1")]
    public void AbsenceType_IsExactlyVacationAppointmentAndOther()
    {
        Enum.GetNames<AbsenceType>().ShouldBe(["Vacation", "Other", "Appointment"], ignoreOrder: true);
    }
}
