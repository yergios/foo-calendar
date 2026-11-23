using Api.Data.Models;

namespace Api.Dtos;

public sealed record AuditEntryDto(
    int Id,
    DateTime OccurredAt,
    string? Actor,
    string? Action,
    string? Target,
    string? Details)
{
    public static AuditEntryDto FromEntity(AuditEntry entry) => new(
        entry.Id,
        entry.OccurredAt,
        entry.Actor,
        entry.Action,
        entry.Target,
        entry.Details);
}

/// <summary><paramref name="Total"/> is the whole log, not this page, so the admin page can
/// say how much further back it goes without asking for all of it.</summary>
public sealed record AuditEntriesResponse(List<AuditEntryDto> Entries, int Total);
