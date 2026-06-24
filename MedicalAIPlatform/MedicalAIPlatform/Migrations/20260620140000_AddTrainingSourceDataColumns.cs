using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260620140000_AddTrainingSourceDataColumns")]
public partial class AddTrainingSourceDataColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddSourceColumn(migrationBuilder, "CheXNetAcceptedData");
        AddSourceColumn(migrationBuilder, "CheXNetModifiedData");
        AddSourceColumn(migrationBuilder, "LungCancerAcceptedData");
        AddSourceColumn(migrationBuilder, "LungCancerModifiedData");
        AddSourceColumn(migrationBuilder, "BioBERTAcceptedData");
        AddSourceColumn(migrationBuilder, "BioBERTModifiedData");

        migrationBuilder.AddColumn<string>(
            name: "TrainingLogPath",
            table: "TrainingJobs",
            type: "nvarchar(1024)",
            maxLength: 1024,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TrainingLogPath", table: "TrainingJobs");
        DropSourceColumn(migrationBuilder, "BioBERTModifiedData");
        DropSourceColumn(migrationBuilder, "BioBERTAcceptedData");
        DropSourceColumn(migrationBuilder, "LungCancerModifiedData");
        DropSourceColumn(migrationBuilder, "LungCancerAcceptedData");
        DropSourceColumn(migrationBuilder, "CheXNetModifiedData");
        DropSourceColumn(migrationBuilder, "CheXNetAcceptedData");
    }

    private static void AddSourceColumn(MigrationBuilder migrationBuilder, string table) =>
        migrationBuilder.AddColumn<string>(
            name: "SourceDataJson",
            table: table,
            type: "nvarchar(max)",
            nullable: true);

    private static void DropSourceColumn(MigrationBuilder migrationBuilder, string table) =>
        migrationBuilder.DropColumn(name: "SourceDataJson", table: table);
}
