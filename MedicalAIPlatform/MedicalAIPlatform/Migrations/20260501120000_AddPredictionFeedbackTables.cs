using System;
using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260501120000_AddPredictionFeedbackTables")]
public partial class AddPredictionFeedbackTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PredictionFeedbacks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SubmittingDoctorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                RelatedJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ClientSessionCorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Modality = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ModelKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                StudyInstanceUid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                SeriesInstanceUid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                OriginalPredictionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                DoctorAction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                CorrectedPrimaryLabel = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                CorrectedPrimaryConfidence = table.Column<double>(type: "float", nullable: true),
                CorrectedProbabilitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ClinicalNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                TrainingAssetPointerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ReviewStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                ReviewedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ReviewNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                ExportedForTrainingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                TrainingExportBatchId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PredictionFeedbacks", x => x.Id);
                table.ForeignKey(
                    name: "FK_PredictionFeedbacks_AspNetUsers_SubmittingDoctorUserId",
                    column: x => x.SubmittingDoctorUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PredictionFeedbacks_ChestAiBackgroundJobs_RelatedJobId",
                    column: x => x.RelatedJobId,
                    principalTable: "ChestAiBackgroundJobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "PredictionFeedbackAuditEntries",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PredictionFeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PredictionFeedbackAuditEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_PredictionFeedbackAuditEntries_AspNetUsers_ActorUserId",
                    column: x => x.ActorUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PredictionFeedbackAuditEntries_PredictionFeedbacks_PredictionFeedbackId",
                    column: x => x.PredictionFeedbackId,
                    principalTable: "PredictionFeedbacks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PredictionFeedbacks_ReviewStatus",
            table: "PredictionFeedbacks",
            column: "ReviewStatus");

        migrationBuilder.CreateIndex(
            name: "IX_PredictionFeedbacks_SubmittingDoctorUserId_CreatedAt",
            table: "PredictionFeedbacks",
            columns: new[] { "SubmittingDoctorUserId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_PredictionFeedbacks_SubmittingDoctorUserId_ClientSessionCorrelationId_ModelKey",
            table: "PredictionFeedbacks",
            columns: new[] { "SubmittingDoctorUserId", "ClientSessionCorrelationId", "ModelKey" });

        migrationBuilder.CreateIndex(
            name: "IX_PredictionFeedbackAuditEntries_PredictionFeedbackId",
            table: "PredictionFeedbackAuditEntries",
            column: "PredictionFeedbackId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PredictionFeedbackAuditEntries");
        migrationBuilder.DropTable(name: "PredictionFeedbacks");
    }
}
