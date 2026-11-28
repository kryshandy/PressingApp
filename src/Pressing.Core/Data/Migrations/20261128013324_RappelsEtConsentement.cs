using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pressing.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RappelsEtConsentement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentGivenAt",
                table: "Clients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentRevokedAt",
                table: "Clients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationPreference",
                table: "Clients",
                type: "TEXT",
                nullable: false,
                defaultValue: "None"); // valeur de l'énumération : "" rendrait les clients existants illisibles

            migrationBuilder.CreateTable(
                name: "ReminderLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReminderType = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_OrderId_ReminderType",
                table: "ReminderLogs",
                columns: new[] { "OrderId", "ReminderType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_SentAt",
                table: "ReminderLogs",
                column: "SentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderLogs");

            migrationBuilder.DropColumn(
                name: "ConsentGivenAt",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ConsentRevokedAt",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "NotificationPreference",
                table: "Clients");
        }
    }
}
