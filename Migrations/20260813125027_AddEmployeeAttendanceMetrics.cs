using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAttendanceMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalAbsents",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalLogIns",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalAbsents",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TotalLogIns",
                table: "Employees");
        }
    }
}
