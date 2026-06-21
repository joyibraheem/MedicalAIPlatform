using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalAIPlatform.Migrations
{
    [Migration("20260619140000_AddDoctorRegistrationProfileFields")]
    /// <inheritdoc />
    public partial class AddDoctorRegistrationProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByAdminId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HospitalOrganization",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MedicalLicenseNumber",
                table: "AspNetUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfileSubmittedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByAdminId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "AspNetUsers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNotes",
                table: "AspNetUsers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApprovedAt", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "ApprovedByAdminId", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "HospitalOrganization", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "MedicalLicenseNumber", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "ProfileSubmittedAt", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "RejectedAt", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "RejectedByAdminId", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "RejectionReason", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "RegistrationNotes", table: "AspNetUsers");
        }
    }
}
