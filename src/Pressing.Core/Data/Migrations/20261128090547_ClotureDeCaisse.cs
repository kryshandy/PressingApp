using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pressing.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClotureDeCaisse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashClosings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BusinessDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpeningFloat = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedCash = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedMobileMoney = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedCard = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedTransfer = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedOther = table.Column<long>(type: "INTEGER", nullable: false),
                    CountedCash = table.Column<long>(type: "INTEGER", nullable: false),
                    Variance = table.Column<long>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Draft"),
                    CreatedByStaffId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedByName = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidatedByStaffId = table.Column<int>(type: "INTEGER", nullable: true),
                    ValidatedByName = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashClosings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashClosings_StaffUsers_CreatedByStaffId",
                        column: x => x.CreatedByStaffId,
                        principalTable: "StaffUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CashClosings_StaffUsers_ValidatedByStaffId",
                        column: x => x.ValidatedByStaffId,
                        principalTable: "StaffUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_BusinessDate_Validated",
                table: "CashClosings",
                column: "BusinessDate",
                unique: true,
                filter: "\"Status\" = 'Validated'");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_CreatedByStaffId",
                table: "CashClosings",
                column: "CreatedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_ValidatedByStaffId",
                table: "CashClosings",
                column: "ValidatedByStaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashClosings");
        }
    }
}
