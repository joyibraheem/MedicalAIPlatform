using System;
using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260620120000_AddModelFineTuningPipeline")]
public partial class AddModelFineTuningPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        CreateTrainingPair(migrationBuilder, "CheXNetAcceptedData", "CheXNetModifiedData");
        CreateTrainingPair(migrationBuilder, "LungCancerAcceptedData", "LungCancerModifiedData");
        CreateTrainingPair(migrationBuilder, "BioBERTAcceptedData", "BioBERTModifiedData");

        migrationBuilder.CreateTable(
            name: "TrainingJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DatasetSize = table.Column<int>(type: "int", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                PreviousModelVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                NewModelVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                Accuracy = table.Column<double>(type: "float", nullable: true),
                F1Score = table.Column<double>(type: "float", nullable: true),
                Loss = table.Column<double>(type: "float", nullable: true),
                DatasetPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                TrainingBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TrainingJobs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_TrainingJobs_ModelName_Status",
            table: "TrainingJobs",
            columns: new[] { "ModelName", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_TrainingJobs_StartedAt",
            table: "TrainingJobs",
            column: "StartedAt");

        migrationBuilder.CreateTable(
            name: "ModelVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                VersionNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TrainingDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DatasetSize = table.Column<int>(type: "int", nullable: false),
                Accuracy = table.Column<double>(type: "float", nullable: true),
                F1Score = table.Column<double>(type: "float", nullable: true),
                Loss = table.Column<double>(type: "float", nullable: true),
                FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                IsProduction = table.Column<bool>(type: "bit", nullable: false),
                IsDeployable = table.Column<bool>(type: "bit", nullable: false),
                TrainingJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ModelVersions", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ModelVersions_ModelName_IsProduction",
            table: "ModelVersions",
            columns: new[] { "ModelName", "IsProduction" });

        migrationBuilder.CreateIndex(
            name: "IX_ModelVersions_ModelName_VersionNumber",
            table: "ModelVersions",
            columns: new[] { "ModelName", "VersionNumber" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ModelVersions");
        migrationBuilder.DropTable(name: "TrainingJobs");
        migrationBuilder.DropTable(name: "BioBERTModifiedData");
        migrationBuilder.DropTable(name: "BioBERTAcceptedData");
        migrationBuilder.DropTable(name: "LungCancerModifiedData");
        migrationBuilder.DropTable(name: "LungCancerAcceptedData");
        migrationBuilder.DropTable(name: "CheXNetModifiedData");
        migrationBuilder.DropTable(name: "CheXNetAcceptedData");
    }

    private static void CreateTrainingPair(MigrationBuilder migrationBuilder, string acceptedTable, string modifiedTable)
    {
        migrationBuilder.CreateTable(
            name: acceptedTable,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                InputDataReference = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                Prediction = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                ConfidenceScore = table.Column<double>(type: "float", nullable: true),
                DoctorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                PatientId = table.Column<int>(type: "int", nullable: true),
                DicomStudyUid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                SourceFeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                TrainingBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsProcessed = table.Column<bool>(type: "bit", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey($"PK_{acceptedTable}", x => x.Id));

        migrationBuilder.CreateIndex(
            name: $"IX_{acceptedTable}_SourceFeedbackId",
            table: acceptedTable,
            column: "SourceFeedbackId",
            unique: true,
            filter: "[SourceFeedbackId] IS NOT NULL");

        migrationBuilder.CreateTable(
            name: modifiedTable,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OriginalPrediction = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                CorrectedPrediction = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                OriginalConfidence = table.Column<double>(type: "float", nullable: true),
                DoctorNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                DoctorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                PatientId = table.Column<int>(type: "int", nullable: true),
                DicomStudyUid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                SourceFeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                TrainingBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsProcessed = table.Column<bool>(type: "bit", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey($"PK_{modifiedTable}", x => x.Id));

        migrationBuilder.CreateIndex(
            name: $"IX_{modifiedTable}_SourceFeedbackId",
            table: modifiedTable,
            column: "SourceFeedbackId",
            unique: true,
            filter: "[SourceFeedbackId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: $"IX_{modifiedTable}_IsProcessed",
            table: modifiedTable,
            column: "IsProcessed");
    }
}
