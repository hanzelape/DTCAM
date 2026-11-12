using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceEncodings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaceEncodings",
                table: "Employees",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceEncodings",
                table: "Employees");
        }
    }
}
