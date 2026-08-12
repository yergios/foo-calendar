using System.ComponentModel.DataAnnotations;

using Api.Configuration;
using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class PersonService(
    AppDbContext db,
    IOptions<PersonColorPaletteOptions> colorPalette) : IPersonService
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

    // Column headers, normalized to letters and digits only and upper-cased, so
    // "Home City", "home_city" and "homeCity" all land on the same column.
    private const string UsernameColumn = "USERNAME";
    private const string FirstNameColumn = "FIRSTNAME";
    private const string LastNameColumn = "LASTNAME";
    private const string DisplayNameColumn = "DISPLAYNAME";
    private const string EmailColumn = "EMAIL";
    private const string HomeCityColumn = "HOMECITY";
    private const string ColorColumn = "COLOR";

    private static string NormalizeColumn(string header) =>
        new string([.. header.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

    public async Task<ImportPeopleResponse> ImportAsync(
        string csv, string actorUsername, CancellationToken ct = default)
    {
        var rows = Csv.Parse(csv);
        if (rows.Count == 0)
            throw new ArgumentException("The file is empty.");

        var columns = rows[0]
            .Select((header, index) => (Name: NormalizeColumn(header), Index: index))
            .Where(c => c.Name.Length > 0)
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.Ordinal);

        if (!columns.ContainsKey(UsernameColumn))
            throw new ArgumentException(
                $"The first line must be a header row including a 'username' column. Found: {string.Join(", ", rows[0])}");

        // Everything is decided in memory against these, then written once at the end.
        var takenUsernames = (await db.People.Select(p => p.Username).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenColors = (await db.People.Select(p => p.Color).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<string>();
        var skipped = new List<ImportPersonIssue>();
        var failed = new List<ImportPersonIssue>();

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1; // 1-based, header included, so it matches the file
            var username = Field(row, columns, UsernameColumn);

            if (username.Length == 0)
            {
                failed.Add(new ImportPersonIssue(line, "", "No username."));
                continue;
            }

            if (!takenUsernames.Add(username))
            {
                skipped.Add(new ImportPersonIssue(
                    line, username, "Already imported, or already exists in the app."));
                continue;
            }

            var color = Field(row, columns, ColorColumn);
            if (color.Length == 0)
            {
                // Assigned rather than demanded: an HR export has no idea what the app's
                // palette is, and colors have to stay unique.
                color = colorPalette.Value.Find(c => !takenColors.Contains(c));
                if (color is null)
                {
                    failed.Add(new ImportPersonIssue(line, username,
                        $"Every color in PersonColorPalette is taken ({colorPalette.Value.Count} in total). Add more to import more people."));
                    continue;
                }
            }
            else if (takenColors.Contains(color))
            {
                failed.Add(new ImportPersonIssue(line, username, $"Color {color} is already taken."));
                continue;
            }

            takenColors.Add(color);

            var firstName = Field(row, columns, FirstNameColumn);
            var lastName = Field(row, columns, LastNameColumn);
            var displayName = Field(row, columns, DisplayNameColumn);
            if (displayName.Length == 0)
                displayName = string.Join(" ", new[] { firstName, lastName }.Where(n => n.Length > 0));
            if (displayName.Length == 0)
                displayName = username;

            // Blank optional columns become null: an empty string would fail the region
            // code pattern, where absent is perfectly valid.
            var request = new CreatePersonRequest(
                username,
                NullIfEmpty(firstName),
                NullIfEmpty(lastName),
                displayName,
                NullIfEmpty(Field(row, columns, HomeCityColumn)),
                color,
                NullIfEmpty(Field(row, columns, EmailColumn)));

            // The same annotations the API validates a single POST against, so an import
            // can't sneak in anything the endpoint would have rejected.
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true))
            {
                failed.Add(new ImportPersonIssue(line, username,
                    string.Join(" ", errors.Select(e => e.ErrorMessage))));
                continue;
            }

            db.People.Add(new Person
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
            });

            created.Add(username);
        }

        await db.SaveChangesAsync(ct);

        return new ImportPeopleResponse(created, skipped, failed);
    }

    private static string Field(string[] row, Dictionary<string, int> columns, string column) =>
        columns.TryGetValue(column, out var index) && index < row.Length
            ? row[index].Trim()
            : "";

    private static string NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
