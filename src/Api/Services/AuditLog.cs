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
        string? target,
        string? details = null,
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
        int limit,
        int offset,
        string? search = null,
        string? action = null,
        DateTime? from = null,
        DateTime? to = null,
        string sortBy = AuditSortColumn.OccurredAt,
        bool descending = true,
        CancellationToken ct = default)
    {
        var query = db.AuditEntries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                EF.Functions.Like(e.Actor, SqlLikePattern.Contains(search), SqlLikePattern.EscapeChar));

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(e => e.Action == action);

        if (from is not null)
            query = query.Where(e => e.OccurredAt >= from);

        if (to is not null)
            query = query.Where(e => e.OccurredAt < to);

        // Ordered by Id, not just OccurredAt/Actor/Action: two entries can share the exact
        // same value (written in the same millisecond, or by the same actor/action), and
        // paging over an ambiguous sort would repeat or skip rows between pages.
        query = (sortBy, descending) switch
        {
            (AuditSortColumn.Actor, false) => query.OrderBy(e => e.Actor).ThenBy(e => e.Id),
            (AuditSortColumn.Actor, true) => query.OrderByDescending(e => e.Actor).ThenByDescending(e => e.Id),
            (AuditSortColumn.Action, false) => query.OrderBy(e => e.Action).ThenBy(e => e.Id),
            (AuditSortColumn.Action, true) => query.OrderByDescending(e => e.Action).ThenByDescending(e => e.Id),
            (_, false) => query.OrderBy(e => e.Id),
            (_, true) => query.OrderByDescending(e => e.Id),
        };

        var total = await query.CountAsync(ct);
        var entries = await query.Skip(offset).Take(limit).ToListAsync(ct);

        return new([.. entries.Select(AuditEntryDto.FromEntity)], total);
    }

    // Never throws the way GetUsername does: failing to write the log would undo the very
    // action being logged, which is a worse outcome than an entry attributed to "system".
    private string CurrentActor() =>
        httpContextAccessor.HttpContext?.User?.FindFirst(authOptions.Value.ClaimTypes.UserName)?.Value
            ?? SystemActor;
}
