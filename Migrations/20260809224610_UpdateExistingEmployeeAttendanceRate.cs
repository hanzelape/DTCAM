using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class UpdateExistingEmployeeAttendanceRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Employees SET AttendanceRate = 100.0 WHERE AttendanceRate = 0.0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Employees SET AttendanceRate = 0.0 WHERE AttendanceRate = 100.0");
        }
    }
}
