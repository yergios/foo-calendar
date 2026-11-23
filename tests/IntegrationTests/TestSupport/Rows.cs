using Api.Data.Models;

namespace IntegrationTests.TestSupport;

/// <summary>Rows for <see cref="TestApp.Seed"/>, with every required column filled in and
/// nothing else, for what the test identity can't create through a route itself.</summary>
public static class Rows
{
    public static Person Person(string username, string? homeCity = null) => new()
    {
        Username = username,
        DisplayName = username,
        HomeCity = homeCity,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };

    public static Absence Vacation(Person person, DateOnly start, DateOnly end) => new()
    {
        Person = person,
        StartDate = start,
        EndDate = end,
        Type = AbsenceType.Vacation,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = person.Username,
    };
}
