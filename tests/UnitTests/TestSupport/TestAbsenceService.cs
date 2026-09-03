using Api.Configuration;
using Api.Data;
using Api.Services;

using Microsoft.Extensions.Options;

namespace UnitTests.TestSupport;

/// <summary>A real AbsenceService against a test database, with recording/no-op fakes for
/// everything external (audit, notifications) and approvals off, matching every deployment
/// until it opts in. For tests of something else (PersonService's stats-sorted paging, for
/// one) that need AbsenceService's actual day-counting rather than a mock of it.</summary>
public static class TestAbsenceService
{
    public static AbsenceService Create(AppDbContext db) => new(
        db,
        new RecordingAuditLog(),
        new RecordingNotificationService(),
        Options.Create(new AbsenceApprovalOptions { Enabled = false }));
}
