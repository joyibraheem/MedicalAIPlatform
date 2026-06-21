using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations
{
    [Migration("20260619160000_AddScanAiAnalysisPipeline")]
    /// <inheritdoc />
    public partial class AddScanAiAnalysisPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanAiAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientScanId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CheXNetResults = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    BioBertResults = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    LungAIResults = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    LinkedModels = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GeneratedResult = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true),
                    ResultGeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BackgroundJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanAiAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanAiAnalyses_ChestAiBackgroundJobs_BackgroundJobId",
                        column: x => x.BackgroundJobId,
                        principalTable: "ChestAiBackgroundJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScanAiAnalyses_PatientScans_PatientScanId",
                        column: x => x.PatientScanId,
                        principalTable: "PatientScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<int>(
                name: "PatientScanId",
                table: "ChestAiBackgroundJobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScanAiAnalysisId",
                table: "ClinicalMedicalReports",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO ScanAiAnalyses (
                    PatientScanId, Status, CheXNetResults, BioBertResults, LungAIResults,
                    LinkedModels, GeneratedResult, ResultGeneratedAt, CreatedByUserId, CreatedAt, CompletedAt)
                SELECT
                    Id,
                    CASE
                        WHEN CheXNetResults IS NOT NULL OR BioBertResults IS NOT NULL
                             OR LungAIResults IS NOT NULL OR GeneratedResult IS NOT NULL
                        THEN N'completed'
                        ELSE N'pending'
                    END,
                    CheXNetResults, BioBertResults, LungAIResults, LinkedModels, GeneratedResult,
                    ResultGeneratedAt, CreatedByUserId, CreatedAt, ResultGeneratedAt
                FROM PatientScans;
                """);

            migrationBuilder.Sql("""
                UPDATE r
                SET r.ScanAiAnalysisId = x.AnalysisId
                FROM ClinicalMedicalReports r
                CROSS APPLY (
                    SELECT TOP (1) a.Id AS AnalysisId
                    FROM ScanAiAnalyses a
                    INNER JOIN PatientScans s ON s.Id = a.PatientScanId
                    WHERE s.PatientId = r.PatientId
                    ORDER BY s.ScanDate DESC
                ) x
                WHERE r.ScanAiAnalysisId IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ChestAiBackgroundJobs_PatientScanId",
                table: "ChestAiBackgroundJobs",
                column: "PatientScanId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalMedicalReports_ScanAiAnalysisId",
                table: "ClinicalMedicalReports",
                column: "ScanAiAnalysisId",
                unique: true,
                filter: "[ScanAiAnalysisId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScanAiAnalyses_BackgroundJobId",
                table: "ScanAiAnalyses",
                column: "BackgroundJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanAiAnalyses_PatientScanId",
                table: "ScanAiAnalyses",
                column: "PatientScanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanAiAnalyses_Status",
                table: "ScanAiAnalyses",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_ChestAiBackgroundJobs_PatientScans_PatientScanId",
                table: "ChestAiBackgroundJobs",
                column: "PatientScanId",
                principalTable: "PatientScans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicalMedicalReports_ScanAiAnalyses_ScanAiAnalysisId",
                table: "ClinicalMedicalReports",
                column: "ScanAiAnalysisId",
                principalTable: "ScanAiAnalyses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(name: "CheXNetResults", table: "PatientScans");
            migrationBuilder.DropColumn(name: "BioBertResults", table: "PatientScans");
            migrationBuilder.DropColumn(name: "LungAIResults", table: "PatientScans");
            migrationBuilder.DropColumn(name: "LinkedModels", table: "PatientScans");
            migrationBuilder.DropColumn(name: "GeneratedResult", table: "PatientScans");
            migrationBuilder.DropColumn(name: "ResultGeneratedAt", table: "PatientScans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheXNetResults",
                table: "PatientScans",
                type: "nvarchar(max)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BioBertResults",
                table: "PatientScans",
                type: "nvarchar(max)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LungAIResults",
                table: "PatientScans",
                type: "nvarchar(max)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedModels",
                table: "PatientScans",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeneratedResult",
                table: "PatientScans",
                type: "nvarchar(max)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultGeneratedAt",
                table: "PatientScans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE s
                SET
                    s.CheXNetResults = a.CheXNetResults,
                    s.BioBertResults = a.BioBertResults,
                    s.LungAIResults = a.LungAIResults,
                    s.LinkedModels = a.LinkedModels,
                    s.GeneratedResult = a.GeneratedResult,
                    s.ResultGeneratedAt = a.ResultGeneratedAt
                FROM PatientScans s
                INNER JOIN ScanAiAnalyses a ON a.PatientScanId = s.Id;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ChestAiBackgroundJobs_PatientScans_PatientScanId",
                table: "ChestAiBackgroundJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_ClinicalMedicalReports_ScanAiAnalyses_ScanAiAnalysisId",
                table: "ClinicalMedicalReports");

            migrationBuilder.DropTable(name: "ScanAiAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_ChestAiBackgroundJobs_PatientScanId",
                table: "ChestAiBackgroundJobs");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalMedicalReports_ScanAiAnalysisId",
                table: "ClinicalMedicalReports");

            migrationBuilder.DropColumn(name: "PatientScanId", table: "ChestAiBackgroundJobs");
            migrationBuilder.DropColumn(name: "ScanAiAnalysisId", table: "ClinicalMedicalReports");
        }
    }
}
