using Api.Configuration;
using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class AuditLog(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthOptions> authOptions) : IAuditLog
{
    /// <summary>Stands in for a username on the scheduled jobs, which act with nobody
    /// signed in. Matches the "&lt;source&gt;-sync" spirit of BankHoliday.CreatedBy.</summary>
    public const string SystemActor = "system";

    public async Task RecordAsync(
        string action,
        string target,
        string details = null,
        CancellationToken ct = default)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = DateTime.UtcNow,
            Actor = CurrentActor(),
            Action = action,
            Target = target,
            Details = details,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<AuditEntriesResponse> ReadAsync(
        int limit, int offset, CancellationToken ct = default)
    {
        // Ordered by Id rather than OccurredAt: two entries written in the same millisecond
        // still have to come back in the order they happened, and paging over an ambiguous
        // sort would repeat or skip rows between pages.
        var entries = await db.AuditEntries
            .OrderByDescending(e => e.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var total = await db.AuditEntries.CountAsync(ct);

        return new([.. entries.Select(AuditEntryDto.FromEntity)], total);
    }

    // Never throws the way GetUsername does: failing to write the log would undo the very
    // action being logged, which is a worse outcome than an entry attributed to "system".
    private string CurrentActor() =>
        httpContextAccessor.HttpContext?.User?.FindFirst(authOptions.Value.ClaimTypes.UserName)?.Value
            ?? SystemActor;
}
