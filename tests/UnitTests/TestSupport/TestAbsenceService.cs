using Api.Data;
using Api.Services;

namespace UnitTests.TestSupport;

/// <summary>A real AbsenceService against a test database, with recording fakes for
/// everything external. For tests of something else (PersonService's stats-sorted paging,
/// for one) that need AbsenceService's actual day-counting rather than a mock of it.</summary>
public static class TestAbsenceService
{
    public static AbsenceService Create(AppDbContext db) => new(
        db,
        new RecordingBankHolidayService(),
        new RecordingAuditLog());
}
