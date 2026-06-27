using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260620180000_AddTrainingCenterExtensions")]
public partial class AddTrainingCenterExtensions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsFavorite",
            table: "ModelVersions",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsPinned",
            table: "ModelVersions",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsProductionCandidate",
            table: "ModelVersions",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsRecommended",
            table: "ModelVersions",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "DatasetVersion",
            table: "ModelVersions",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DatasetHash",
            table: "ModelVersions",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ValidationStatus",
            table: "ModelVersions",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TrainingReportPath",
            table: "ModelVersions",
            type: "nvarchar(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VersionLabel",
            table: "DatasetArchiveEntries",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContentHash",
            table: "DatasetArchiveEntries",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ValidationStatus",
            table: "DatasetArchiveEntries",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ValidationStatus", table: "DatasetArchiveEntries");
        migrationBuilder.DropColumn(name: "ContentHash", table: "DatasetArchiveEntries");
        migrationBuilder.DropColumn(name: "VersionLabel", table: "DatasetArchiveEntries");
        migrationBuilder.DropColumn(name: "TrainingReportPath", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "ValidationStatus", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "DatasetHash", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "DatasetVersion", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "IsRecommended", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "IsProductionCandidate", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "IsPinned", table: "ModelVersions");
        migrationBuilder.DropColumn(name: "IsFavorite", table: "ModelVersions");
    }
}
