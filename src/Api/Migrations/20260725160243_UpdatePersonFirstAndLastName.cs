using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePersonFirstAndLastName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_Auth0Id",
                table: "People");

            migrationBuilder.RenameColumn(
                name: "Auth0Id",
                table: "People",
                newName: "LastName");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "People",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "People");

            migrationBuilder.RenameColumn(
                name: "LastName",
                table: "People",
                newName: "Auth0Id");

            migrationBuilder.CreateIndex(
                name: "IX_People_Auth0Id",
                table: "People",
                column: "Auth0Id",
                unique: true);
        }
    }
}
