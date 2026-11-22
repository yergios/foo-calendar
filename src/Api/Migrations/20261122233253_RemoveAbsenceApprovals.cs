using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAbsenceApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Approvals are gone (docs/decisions/0014), and with them every status: what is
            // left of an absence is in effect. A rejected one never was, and the calendar
            // already hid it, so it goes rather than reappearing. Pending ones stay, as
            // ordinary absences. Status 2 was AbsenceStatus.Rejected.
            migrationBuilder.Sql("DELETE FROM \"Absences\" WHERE \"Status\" = 2;");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "Absences");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Absences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Absences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Absences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "Absences",
                type: "TEXT",
                nullable: true);

            // 1 was AbsenceStatus.Approved: every absence that survives Up is in effect.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Absences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsenceType = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_PersonId_Id",
                table: "Notifications",
                columns: new[] { "PersonId", "Id" });
        }
    }
}
