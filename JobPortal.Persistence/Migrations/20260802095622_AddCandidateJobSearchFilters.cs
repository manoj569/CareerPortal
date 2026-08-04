using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateJobSearchFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Jobs",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EducationRequirement",
                table: "Jobs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InternshipDurationMonths",
                table: "Jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlexibleDuration",
                table: "Jobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaximumExperienceYears",
                table: "Jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumExperienceYears",
                table: "Jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostedByType",
                table: "Jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleCategory",
                table: "Jobs",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyType",
                table: "Companies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Department",
                table: "Jobs",
                column: "Department");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_RoleCategory",
                table: "Jobs",
                column: "RoleCategory");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status_PostedByType",
                table: "Jobs",
                columns: new[] { "Status", "PostedByType" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status_WorkplaceType_EmploymentType",
                table: "Jobs",
                columns: new[] { "Status", "WorkplaceType", "EmploymentType" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Jobs_ExperienceRange",
                table: "Jobs",
                sql: "[MinimumExperienceYears] IS NULL OR [MaximumExperienceYears] IS NULL OR [MinimumExperienceYears] <= [MaximumExperienceYears]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Jobs_InternshipDuration",
                table: "Jobs",
                sql: "[InternshipDurationMonths] IS NULL OR [InternshipDurationMonths] IN (1, 2, 3, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CompanyType_Industry",
                table: "Companies",
                columns: new[] { "CompanyType", "Industry" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_Department",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_RoleCategory",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_Status_PostedByType",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_Status_WorkplaceType_EmploymentType",
                table: "Jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Jobs_ExperienceRange",
                table: "Jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Jobs_InternshipDuration",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Companies_CompanyType_Industry",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EducationRequirement",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "InternshipDurationMonths",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IsFlexibleDuration",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MaximumExperienceYears",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MinimumExperienceYears",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PostedByType",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "RoleCategory",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CompanyType",
                table: "Companies");
        }
    }
}
