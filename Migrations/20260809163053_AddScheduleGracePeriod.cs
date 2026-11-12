using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleGracePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LateGracePeriodMinutes",
                table: "Schedules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LateGracePeriodMinutes",
                table: "Schedules");
        }
    }
}
