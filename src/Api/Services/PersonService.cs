using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class PersonService(AppDbContext db) : IPersonService
{
    public async Task<PersonDto> CreateAsync(
        CreatePersonRequest request, string actorUsername, CancellationToken ct = default)
    {
        if (await db.People.AnyAsync(p => p.Username == request.Username, ct))
            throw new InvalidOperationException("A person already exists for this account.");

        if (!await IsColorAvailableAsync(request.Color, excludingPersonId: null, ct))
            throw new InvalidOperationException("Color is already taken by another person.");

        var person = new Person
        {
            Username = request.Username,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            HomeCity = request.HomeCity,
            Color = request.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorUsername,
        };

        db.People.Add(person);
        await db.SaveChangesAsync(ct);
        return PersonDto.FromEntity(person);
    }

    public async Task<PeopleResponse> GetAllAsync(CancellationToken ct = default)
    {
        var people = await db.People.OrderBy(p => p.DisplayName ?? p.Username).ToListAsync(ct);
        return new([.. people.Select(PersonDto.FromEntity)]);
    }

    public async Task<ColorsInUseResponse> GetColorsInUseAsync(CancellationToken ct = default)
    {
        var colors = await db.People.Select(p => p.Color).ToListAsync(ct);
        return new(colors);
    }

    public async Task DeleteAsync(int personId, CancellationToken ct = default)
    {
        var person = await db.People.FindAsync([personId], ct)
            ?? throw new KeyNotFoundException($"Person {personId} not found.");

        db.People.Remove(person);
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> IsColorAvailableAsync(string color, int? excludingPersonId, CancellationToken ct = default)
    {
        var query = db.People.Where(p => p.Color == color);
        if (excludingPersonId is int id)
            query = query.Where(p => p.Id != id);

        return !await query.AnyAsync(ct);
    }

    public async Task<PersonDto> UpdateAsync(
        int personId,
        UpdatePersonRequest request,
        string actorUsername,
        CancellationToken ct = default)
    {
        var person = await db.People.FindAsync([personId], ct)
            ?? throw new KeyNotFoundException($"Person {personId} not found.");

        if (!await IsColorAvailableAsync(request.Color, personId, ct))
            throw new InvalidOperationException("Color is already taken by another person.");

        person.DisplayName = request.DisplayName;
        person.Color = request.Color;
        person.HomeCity = request.HomeCity;
        person.UpdatedAt = DateTime.UtcNow;
        person.UpdatedBy = actorUsername;
        await db.SaveChangesAsync(ct);
        return PersonDto.FromEntity(person);
    }
}
