using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pressing.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecuritePersonnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "StaffUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "StaffUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryCodeHashes",
                table: "StaffUsers",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "StaffUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TwoFactorLastStep",
                table: "StaffUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "StaffUsers",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeHashes",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorLastStep",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "StaffUsers");
        }
    }
}
