using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260620160000_AddTrainingCenterEnterpriseFeatures")]
public partial class AddTrainingCenterEnterpriseFeatures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RequestedByUserId",
            table: "TrainingJobs",
            type: "nvarchar(450)",
            maxLength: 450,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "QueuePosition",
            table: "TrainingJobs",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "HyperparametersJson",
            table: "TrainingJobs",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "EpochsConfigured",
            table: "TrainingJobs",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "LearningRate",
            table: "TrainingJobs",
            type: "float",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BatchSizeConfigured",
            table: "TrainingJobs",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExperimentName",
            table: "TrainingJobs",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Notes",
            table: "ModelVersions",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TrainingCenterNotifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                NotificationType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                IsRead = table.Column<bool>(type: "bit", nullable: false),
                RelatedJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_TrainingCenterNotifications", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_TrainingCenterNotifications_CreatedAt",
            table: "TrainingCenterNotifications",
            column: "CreatedAt");

        migrationBuilder.CreateTable(
            name: "DatasetArchiveEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                PathHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UploadDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ImageCount = table.Column<int>(type: "int", nullable: false),
                PatientCount = table.Column<int>(type: "int", nullable: false),
                NormalCount = table.Column<int>(type: "int", nullable: false),
                PositiveCount = table.Column<int>(type: "int", nullable: false),
                DiseaseDistributionJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                UsedModelsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                TrainingRunCount = table.Column<int>(type: "int", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_DatasetArchiveEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DatasetArchiveEntries_PathHash",
            table: "DatasetArchiveEntries",
            column: "PathHash",
            unique: true);

        migrationBuilder.CreateTable(
            name: "DeploymentHistoryRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                FromVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ToVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ToCheckpointId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                DeployedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DeployedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
            },
            constraints: table => table.PrimaryKey("PK_DeploymentHistoryRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DeploymentHistoryRecords_ModelName_DeployedAt",
            table: "DeploymentHistoryRecords",
            columns: new[] { "ModelName", "DeployedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DeploymentHistoryRecords");
        migrationBuilder.DropTable(name: "DatasetArchiveEntries");
        migrationBuilder.DropTable(name: "TrainingCenterNotifications");
        migrationBuilder.DropColumn(name: "Notes", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "ExperimentName", table: "TrainingJobs");
        migrationBuilder.DropColumn(name: "BatchSizeConfigured", table: "TrainingJobs");
        migrationBuilder.DropColumn(name: "LearningRate", table: "TrainingJobs");
        migrationBuilder.DropColumn(name: "EpochsConfigured", table: "TrainingJobs");
        migrationBuilder.DropColumn(name: "HyperparametersJson", table: "TrainingJobs");
        migrationBuilder.DropColumn(name: "QueuePosition", table: "TrainingJobs");
        migrationBuilder.DropColumn(name: "RequestedByUserId", table: "TrainingJobs");
    }
}
