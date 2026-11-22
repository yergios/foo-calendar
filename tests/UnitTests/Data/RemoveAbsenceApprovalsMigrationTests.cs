using Api.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Shouldly;

namespace UnitTests.Data;

/// <summary>The one migration that deletes data, run against a database as the release
/// before it left one: an absence in each of the three statuses approvals had.</summary>
public sealed class RemoveAbsenceApprovalsMigrationTests : IDisposable
{
    private const string PreviousMigration = "20260903152500_RemovePersonEmailAndNotificationPreferences";

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly AppDbContext db;

    public RemoveAbsenceApprovalsMigrationTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    [Trait("Spec", "ABS-7")]
    public void Up_KeepsPendingAndApprovedAbsences_DeletesRejectedOnes_AndDropsNotifications()
    {
        var migrator = db.GetService<IMigrator>();
        migrator.Migrate(PreviousMigration);

        // Status as it was stored: 0 Pending, 1 Approved, 2 Rejected.
        db.Database.ExecuteSqlRaw("""
            INSERT INTO People (Id, Username, DisplayName) VALUES (1, 'jdoe', 'Jane Doe');
            INSERT INTO Absences (Id, PersonId, StartDate, EndDate, Type, Status, CreatedBy)
            VALUES (1, 1, '2026-03-02', '2026-03-03', 0, 0, 'jdoe'),
                   (2, 1, '2026-04-06', '2026-04-07', 0, 1, 'jdoe'),
                   (3, 1, '2026-05-04', '2026-05-05', 0, 2, 'jdoe');
            INSERT INTO Notifications (PersonId, Type, AbsenceType, StartDate, EndDate)
            VALUES (1, 1, 0, '2026-05-04', '2026-05-05');
            """);

        migrator.Migrate();

        db.Absences.Select(a => a.Id).OrderBy(id => id).ToList().ShouldBe([1, 2]);
        db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToList().ShouldNotContain("Notifications");
        db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Absences')")
            .ToList().ShouldBe(
                ["Id", "PersonId", "StartDate", "EndDate", "Type", "Note", "CreatedAt", "CreatedBy",
                    "UpdatedAt", "UpdatedBy", "EndTime", "StartTime"],
                ignoreOrder: true);
    }
}
