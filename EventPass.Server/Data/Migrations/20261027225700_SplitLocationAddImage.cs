using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPass.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitLocationAddImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Modificat manual: EF Core generase DropColumn + AddColumn, ceea ce ar fi
            // pierdut datele. Redenumim Location în Venue și păstrăm ce era scris acolo.
            migrationBuilder.RenameColumn(
                name: "Location",
                table: "Events",
                newName: "Venue");

            migrationBuilder.AlterColumn<string>(
                name: "Venue",
                table: "Events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_City",
                table: "Events",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_Events_IsPublished_StartsAt",
                table: "Events",
                columns: new[] { "IsPublished", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_City",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_IsPublished_StartsAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Events");

            migrationBuilder.AlterColumn<string>(
                name: "Venue",
                table: "Events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.RenameColumn(
                name: "Venue",
                table: "Events",
                newName: "Location");
        }
    }
}
