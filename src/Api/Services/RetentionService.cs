using Api.Configuration;
using Api.Data;
using Api.Infrastructure;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>Enforces Retention:* by deleting what has outlived it. Storage limitation is
/// the one data protection duty software can actually carry out on the deployer's behalf -
/// the rest of it is their paperwork - so this exists to make "we keep it no longer than
/// necessary" a thing the app does rather than a thing they have to remember to do.
///
/// Every deletion is logged, and deliberately not audited: writing an audit entry about
/// deleting audit entries would leave the log growing at exactly the rate this trims it.
/// </summary>
public sealed class RetentionService(
    AppDbContext db,
    IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger) : IRetentionService
{
    public async Task<RetentionSweepResult> SweepAsync(CancellationToken ct = default)
    {
        using var span = AppTelemetry.ActivitySource.StartActivity(AppTelemetry.RetentionSweepSpan);

        var config = options.Value;
        var now = DateTime.UtcNow;

        // ExecuteDeleteAsync rather than loading and tracking: this can be a lot of rows on
        // its first run against a database that predates the setting, and none of them are
        // read on the way out. Neither table has dependents, so there is no cascade to miss.
        var auditEntries = config.AuditEntryDays == RetentionOptions.Never
            ? 0
            : await db.AuditEntries
                .Where(e => e.OccurredAt < now.AddDays(-config.AuditEntryDays))
                .ExecuteDeleteAsync(ct);

        // Measured from when the absence ended, not when it was created: a year-long leave
        // booked two years ago is still current if it runs until next month.
        var absences = 0;
        if (config.AbsenceDays != RetentionOptions.Never)
        {
            var cutoff = DateOnly.FromDateTime(now.AddDays(-config.AbsenceDays));
            absences = await db.Absences.Where(a => a.EndDate < cutoff).ExecuteDeleteAsync(ct);
        }

        var result = new RetentionSweepResult(auditEntries, absences);

        // Every sweep, zero included, so a table's series exists from the first run and a
        // sweep that stopped deleting reads as zeros rather than as no data.
        span?.SetTag(AppTelemetry.AuditEntriesDeletedAttribute, result.AuditEntries);
        span?.SetTag(AppTelemetry.AbsencesDeletedAttribute, result.Absences);
        AppTelemetry.RetentionDeletedRows.Add(result.AuditEntries,
            new KeyValuePair<string, object?>(AppTelemetry.TableAttribute, AppTelemetry.AuditEntriesTable));
        AppTelemetry.RetentionDeletedRows.Add(result.Absences,
            new KeyValuePair<string, object?>(AppTelemetry.TableAttribute, AppTelemetry.AbsencesTable));

        // Logging every sweep would be a daily line saying nothing; logging only the ones
        // that deleted something is what makes the line worth reading when it appears.
        if (result.Total > 0)
            logger.LogInformation(
                "Retention sweep deleted {AuditEntries} audit entries, {Absences} absences.",
                result.AuditEntries, result.Absences);

        return result;
    }
}
