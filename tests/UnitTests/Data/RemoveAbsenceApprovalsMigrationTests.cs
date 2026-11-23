using Api.Data;
using Api.Data.Models;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Shouldly;

namespace UnitTests.Data;

/// <summary>The one migration that deletes data, run against a database as the release
/// before it left one: an absence in each of the three statuses approvals had, and one with
/// every optional column filled in.</summary>
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

    private IMigrator MigratedToThePreviousRelease()
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
            INSERT INTO Absences (Id, PersonId, StartDate, EndDate, Type, Status, Note,
                                  StartTime, EndTime, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy,
                                  ReviewedBy, ReviewedAt, ReviewNote)
            VALUES (4, 1, '2026-06-01', '2026-06-01', 2, 1, 'Dentist',
                    '09:00:00', '10:30:00', '2026-05-20 08:15:00', 'jdoe', '2026-05-21 12:00:00', 'admin',
                    'boss', '2026-05-22 09:00:00', 'Fine');
            INSERT INTO Notifications (PersonId, Type, AbsenceType, StartDate, EndDate)
            VALUES (1, 1, 0, '2026-05-04', '2026-05-05');
            """);

        return migrator;
    }

    private List<string> Query(string sql) => db.Database.SqlQueryRaw<string>(sql).ToList();

    [Fact]
    [Trait("Spec", "ABS-7")]
    public void Up_KeepsPendingAndApprovedAbsences_DeletesRejectedOnes_AndDropsNotifications()
    {
        MigratedToThePreviousRelease().Migrate();

        db.Absences.Select(a => a.Id).OrderBy(id => id).ToList().ShouldBe([1, 2, 4]);
        Query("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ShouldNotContain("Notifications");
        Query("SELECT name AS Value FROM pragma_table_info('Absences')")
            .ShouldBe(
                ["Id", "PersonId", "StartDate", "EndDate", "Type", "Note", "CreatedAt", "CreatedBy",
                    "UpdatedAt", "UpdatedBy", "EndTime", "StartTime"],
                ignoreOrder: true);
    }

    // Dropping a column makes SQLite rebuild the table, which is where a value, an index or
    // a foreign key would be lost if the migration got it wrong.
    [Fact]
    [Trait("Spec", "ABS-7")]
    public void Up_KeepsEveryRemainingValueOfAKeptAbsence()
    {
        MigratedToThePreviousRelease().Migrate();

        var kept = db.Absences.AsNoTracking().Single(a => a.Id == 4);
        kept.PersonId.ShouldBe(1);
        kept.StartDate.ShouldBe(new DateOnly(2026, 6, 1));
        kept.EndDate.ShouldBe(new DateOnly(2026, 6, 1));
        kept.Type.ShouldBe(AbsenceType.Appointment);
        kept.Note.ShouldBe("Dentist");
        kept.StartTime.ShouldBe(new TimeOnly(9, 0));
        kept.EndTime.ShouldBe(new TimeOnly(10, 30));
        kept.CreatedAt.ShouldBe(new DateTime(2026, 5, 20, 8, 15, 0, DateTimeKind.Unspecified));
        kept.CreatedBy.ShouldBe("jdoe");
        kept.UpdatedAt.ShouldBe(new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Unspecified));
        kept.UpdatedBy.ShouldBe("admin");
    }

    [Fact]
    [Trait("Spec", "ABS-7")]
    public void Up_KeepsTheRangeIndexAndTheCascadeFromPeople()
    {
        MigratedToThePreviousRelease().Migrate();

        Query("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Absences'")
            .ShouldContain("IX_Absences_PersonId_StartDate_EndDate");

        // Through SQL rather than the context, so it is the database doing the cascade and not
        // EF deleting tracked dependents itself.
        db.Database.ExecuteSqlRaw("DELETE FROM People WHERE Id = 1");

        Query("SELECT CAST(COUNT(*) AS TEXT) AS Value FROM Absences").ShouldBe(["0"]);
    }
}
