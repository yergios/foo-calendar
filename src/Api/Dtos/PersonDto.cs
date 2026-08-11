using Api.Data.Models;

namespace Api.Dtos;

public sealed record PersonDto(
    int Id,
    string Username,
    string DisplayName,
    string HomeCity,
    string Color)
{
    public static PersonDto FromEntity(Person person) => new(
        person.Id,
        person.Username,
        person.DisplayName,
        person.HomeCity,
        person.Color);
}
