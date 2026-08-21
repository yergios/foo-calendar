using Api.Configuration;
using Api.Data;
using Api.Services;

using Microsoft.Extensions.Options;

namespace UnitTests.TestSupport;

/// <summary>A real AbsenceService against a test database, with recording/no-op fakes for
/// everything external (webhooks, audit, notifications, email) and approvals off, matching
/// every deployment until it opts in. For tests of something else (PersonService's
/// stats-sorted paging, for one) that need AbsenceService's actual day-counting rather
/// than a mock of it.</summary>
public static class TestAbsenceService
{
    public static AbsenceService Create(AppDbContext db) => new(
        db,
        new RecordingWebhookPublisher(),
        new RecordingAuditLog(),
        new RecordingNotificationService(),
        new RecordingEmailSender(),
        Options.Create(new AbsenceApprovalOptions { Enabled = false }),
        Options.Create(new BrandingOptions { AppName = "Foo Calendar" }),
        Options.Create(new EmailOptions()));
}
