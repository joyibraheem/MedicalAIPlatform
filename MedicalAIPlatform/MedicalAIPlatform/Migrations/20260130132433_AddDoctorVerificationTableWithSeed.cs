using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MedicalAIPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorVerificationTableWithSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DoctorStatus",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DoctorVerificationCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false),
                    UsedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorVerificationCodes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DoctorVerificationCodes",
                columns: new[] { "Id", "Code", "CreatedAt", "IsUsed", "UsedAt", "UsedByUserId" },
                values: new object[,]
                {
                    { 1, "DOC-2026-001", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, null, null },
                    { 2, "DOC-2026-002", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, null, null },
                    { 3, "DOC-2026-003", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorVerificationCodes");

            migrationBuilder.DropColumn(
                name: "DoctorStatus",
                table: "AspNetUsers");
        }
    }
}
