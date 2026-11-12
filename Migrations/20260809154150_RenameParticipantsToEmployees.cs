using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaceAttendanceSystem.Migrations
{
    /// <inheritdoc />
    public partial class RenameParticipantsToEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Participants_ParticipantId",
                table: "Attendances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Participants",
                table: "Participants");

            migrationBuilder.RenameTable(
                name: "Participants",
                newName: "Employees");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_EmployeeId",
                table: "Employees",
                newName: "IX_Employees_EmployeeId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Employees",
                table: "Employees",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Employees_ParticipantId",
                table: "Attendances",
                column: "ParticipantId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_ParticipantId",
                table: "Attendances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Employees",
                table: "Employees");

            migrationBuilder.RenameTable(
                name: "Employees",
                newName: "Participants");

            migrationBuilder.RenameIndex(
                name: "IX_Employees_EmployeeId",
                table: "Participants",
                newName: "IX_Participants_EmployeeId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Participants",
                table: "Participants",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Participants_ParticipantId",
                table: "Attendances",
                column: "ParticipantId",
                principalTable: "Participants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
