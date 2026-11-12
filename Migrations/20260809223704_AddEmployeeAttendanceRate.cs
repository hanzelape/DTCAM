using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAttendanceRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "Schedules");

            migrationBuilder.AddColumn<decimal>(
                name: "AttendanceRate",
                table: "Employees",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceRate",
                table: "Employees");

            migrationBuilder.AddColumn<int>(
                name: "AssignmentMode",
                table: "Schedules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
