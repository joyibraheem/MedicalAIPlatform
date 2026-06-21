using System;
using MedicalAIPlatform.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260502183000_AddClinicalMedicalReports")]
public partial class AddClinicalMedicalReports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClinicalMedicalReports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatientId = table.Column<int>(type: "int", nullable: false),
                GeneratedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AiSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                FindingsDisplay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ImpressionDisplay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RecommendationsDisplay = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ConfidenceSnapshot = table.Column<double>(type: "float", nullable: false),
                ModelVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                IsDoctorModified = table.Column<bool>(type: "bit", nullable: false),
                DoctorModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DoctorModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClinicalMedicalReports", x => x.Id);
                table.ForeignKey(
                    name: "FK_ClinicalMedicalReports_Patients_PatientId",
                    column: x => x.PatientId,
                    principalTable: "Patients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MedicalReportRevisions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Findings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Impression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Recommendations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()"),
                ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MedicalReportRevisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_MedicalReportRevisions_ClinicalMedicalReports_ReportId",
                    column: x => x.ReportId,
                    principalTable: "ClinicalMedicalReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ClinicalMedicalReports_PatientId_GeneratedAt",
            table: "ClinicalMedicalReports",
            columns: new[] { "PatientId", "GeneratedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_MedicalReportRevisions_ReportId",
            table: "MedicalReportRevisions",
            column: "ReportId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MedicalReportRevisions");
        migrationBuilder.DropTable(name: "ClinicalMedicalReports");
    }
}
