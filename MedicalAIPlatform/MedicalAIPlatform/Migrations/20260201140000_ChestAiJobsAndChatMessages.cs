using System;
using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260201140000_ChestAiJobsAndChatMessages")]
public partial class ChestAiJobsAndChatMessages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChestAiBackgroundJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                InputPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ResultPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChestAiBackgroundJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChestAiBackgroundJobs_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChestAiChatMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                RelatedJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChestAiChatMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChestAiChatMessages_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChestAiBackgroundJobs_UserId",
            table: "ChestAiBackgroundJobs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ChestAiBackgroundJobs_UserId_CreatedAt",
            table: "ChestAiBackgroundJobs",
            columns: new[] { "UserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ChestAiChatMessages_UserId_CreatedAt",
            table: "ChestAiChatMessages",
            columns: new[] { "UserId", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ChestAiChatMessages");
        migrationBuilder.DropTable(name: "ChestAiBackgroundJobs");
    }
}
