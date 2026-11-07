using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventPass.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderEmailStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TicketsEmailAttempts",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TicketsEmailLastAttemptAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TicketsEmailedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TicketsEmailAttempts",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TicketsEmailLastAttemptAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TicketsEmailedAt",
                table: "Orders");
        }
    }
}
