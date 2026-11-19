using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <summary>
    /// Adds login lockout state, the forced password change flag, and
    /// check-in audit columns (coordinates, matched location, client IP,
    /// liveness result).
    ///
    /// Note: the generator also emitted RenameTable / PK / FK churn because the
    /// previous model snapshot still used PascalCase table names while the
    /// DbContext maps lowercase ones (matching the real database). Those
    /// operations were removed on purpose; this migration only adds columns.
    /// </summary>
    public partial class AddSecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "employees",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "admins",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "admins",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "admins",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "admins",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CheckInLatitude",
                table: "attendances",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CheckInLongitude",
                table: "attendances",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CheckInAccuracyMeters",
                table: "attendances",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckInLocationId",
                table: "attendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CheckInDistanceMeters",
                table: "attendances",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckInIpAddress",
                table: "attendances",
                type: "varchar(64)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "LivenessPassed",
                table: "attendances",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "admins");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "admins");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "admins");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "admins");

            migrationBuilder.DropColumn(
                name: "CheckInLatitude",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "CheckInLongitude",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "CheckInAccuracyMeters",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "CheckInLocationId",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "CheckInDistanceMeters",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "CheckInIpAddress",
                table: "attendances");

            migrationBuilder.DropColumn(
                name: "LivenessPassed",
                table: "attendances");
        }
    }
}
