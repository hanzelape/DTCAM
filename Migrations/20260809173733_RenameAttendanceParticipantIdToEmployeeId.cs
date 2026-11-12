using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class RenameAttendanceParticipantIdToEmployeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_ParticipantId",
                table: "Attendances");

            migrationBuilder.RenameColumn(
                name: "ParticipantId",
                table: "Attendances",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_Attendances_ParticipantId",
                table: "Attendances",
                newName: "IX_Attendances_EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Employees_EmployeeId",
                table: "Attendances",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_EmployeeId",
                table: "Attendances");

            migrationBuilder.RenameColumn(
                name: "EmployeeId",
                table: "Attendances",
                newName: "ParticipantId");

            migrationBuilder.RenameIndex(
                name: "IX_Attendances_EmployeeId",
                table: "Attendances",
                newName: "IX_Attendances_ParticipantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Employees_ParticipantId",
                table: "Attendances",
                column: "ParticipantId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
