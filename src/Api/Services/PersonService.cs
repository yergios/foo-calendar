using System.ComponentModel.DataAnnotations;

using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class PersonService(
    AppDbContext db,
    ITeamRegionService teamRegions,
    RegionLabels regionLabels,
    IAuditLog audit) : IPersonService
{
    // What a person was, for a log entry that still reads sensibly once they are gone.
    private static string Describe(Person person) =>
        person.HomeCity is null ? person.Username : $"{person.Username}, {person.HomeCity}";


    public async Task<PersonDto> CreateAsync(
        CreatePersonRequest request, string actorUsername, CancellationToken ct = default)
    {
        if (await db.People.AnyAsync(p => p.Username == request.Username, ct))
            throw new InvalidOperationException("A person already exists for this account.");

        var person = new Person
        {
            Username = request.Username,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            HomeCity = request.HomeCity,
            EmailOnAbsenceReviewed = request.EmailOnAbsenceReviewed,
            EmailOnNewAbsenceRequest = request.EmailOnNewAbsenceRequest,
            EmailOnWeeklyDigest = request.EmailOnWeeklyDigest,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorUsername,
        };

        db.People.Add(person);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditActions.PersonCreated, person.DisplayName, Describe(person), ct);

        // Somewhere new to the team brings its holidays in with it.
        await teamRegions.EnsureSyncedAsync(person.HomeCity, ct);

        return PersonDto.FromEntity(person, includePreferences: true);
    }

    public async Task<PeopleResponse> GetAllAsync(CancellationToken ct = default)
    {
        var people = await db.People.OrderBy(p => p.DisplayName ?? p.Username).ToListAsync(ct);

        // Countries as well as regions, so the region filter can group "CH-ZH" and "CH-BE"
        // under Switzerland and offer the country itself as "everyone in it".
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var region in await teamRegions.InUseAsync(ct))
        {
            labels[region] = regionLabels.For(region);

            var country = RegionCode.CountryOf(region);
            labels[country] = regionLabels.For(country);
        }

        return new([.. people.Select(p => PersonDto.FromEntity(p, includePreferences: false))], labels);
    }

    public async Task<PeoplePageResponse> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        // Same ordering as GetAllAsync, so a person's position doesn't shuffle between
        // pages just because a different endpoint answered the request.
        var query = db.People.OrderBy(p => p.DisplayName ?? p.Username);

        var total = await query.CountAsync(ct);
        var people = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new([.. people.Select(p => PersonDto.FromEntity(p, includePreferences: false))], total);
    }

    public async Task DeleteAsync(int personId, CancellationToken ct = default)
    {
        var person = await db.People.FindAsync([personId], ct)
            ?? throw new KeyNotFoundException($"Person {personId} not found.");

        var vacated = person.HomeCity;

        // Counted before the cascade takes them, since the whole point of the entry is
        // that there is nothing left to count afterwards.
        var absences = await db.Absences.CountAsync(a => a.PersonId == personId, ct);

        db.People.Remove(person);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            AuditActions.PersonDeleted,
            person.DisplayName,
            $"{Describe(person)}; {absences} absence(s) deleted with them",
            ct);

        // After the delete, so "is anyone still there" is answered by the database rather
        // than by counting this person out by hand.
        await teamRegions.PruneIfUnusedAsync(vacated, ct);
    }

    public async Task<PersonDto> UpdateAsync(
        int personId,
        UpdatePersonRequest request,
        string actorUsername,
        CancellationToken ct = default)
    {
        var person = await db.People.FindAsync([personId], ct)
            ?? throw new KeyNotFoundException($"Person {personId} not found.");

        var vacated = person.HomeCity;

        // Captured before the assignments below overwrite them. Nothing is logged when
        // nothing moved, so a no-op save doesn't pad the log.
        var changes = new List<string>();
        if (person.DisplayName != request.DisplayName)
            changes.Add($"name {person.DisplayName} → {request.DisplayName}");
        if (!string.Equals(person.HomeCity, request.HomeCity, StringComparison.Ordinal))
            changes.Add($"region {person.HomeCity ?? "none"} → {request.HomeCity ?? "none"}");

        person.DisplayName = request.DisplayName;
        person.HomeCity = request.HomeCity;
        person.EmailOnAbsenceReviewed = request.EmailOnAbsenceReviewed;
        person.EmailOnNewAbsenceRequest = request.EmailOnNewAbsenceRequest;
        person.EmailOnWeeklyDigest = request.EmailOnWeeklyDigest;
        person.UpdatedAt = DateTime.UtcNow;
        person.UpdatedBy = actorUsername;
        await db.SaveChangesAsync(ct);

        if (changes.Count > 0)
            await audit.RecordAsync(
                AuditActions.PersonUpdated, person.DisplayName, string.Join("; ", changes), ct);

        // Moving is both halves at once: the new region may be new to the team, and the
        // old one may have just lost its last occupant.
        if (!string.Equals(vacated, person.HomeCity, StringComparison.Ordinal))
        {
            await teamRegions.EnsureSyncedAsync(person.HomeCity, ct);
            await teamRegions.PruneIfUnusedAsync(vacated, ct);
        }

        return PersonDto.FromEntity(person, includePreferences: true);
    }

    // Column headers, normalized to letters and digits only and upper-cased, so
    // "Home City", "home_city" and "homeCity" all land on the same column.
    private const string UsernameColumn = "USERNAME";
    private const string FirstNameColumn = "FIRSTNAME";
    private const string LastNameColumn = "LASTNAME";
    private const string DisplayNameColumn = "DISPLAYNAME";
    private const string EmailColumn = "EMAIL";
    private const string HomeCityColumn = "HOMECITY";

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

        var created = new List<string>();
        var skipped = new List<ImportPersonIssue>();
        var failed = new List<ImportPersonIssue>();
        // Deduplicated up front: an import of fifty people from three offices should cost
        // three syncs, not fifty.
        var arrivedIn = new HashSet<string>(StringComparer.Ordinal);

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
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actorUsername,
            });

            created.Add(username);

            if (request.HomeCity is not null)
                arrivedIn.Add(request.HomeCity);
        }

        await db.SaveChangesAsync(ct);

        // One entry for the file rather than one per row: the individual people are on the
        // people list, and what an import is worth knowing about later is that it happened,
        // by whom, and how much of it landed.
        await audit.RecordAsync(
            AuditActions.PeopleImported,
            $"{created.Count} person(s) imported",
            $"{created.Count} created, {skipped.Count} skipped, {failed.Count} failed",
            ct);

        foreach (var region in arrivedIn)
            await teamRegions.EnsureSyncedAsync(region, ct);

        return new ImportPeopleResponse(created, skipped, failed);
    }

    private static string Field(string[] row, Dictionary<string, int> columns, string column) =>
        columns.TryGetValue(column, out var index) && index < row.Length
            ? row[index].Trim()
            : "";

    private static string NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
