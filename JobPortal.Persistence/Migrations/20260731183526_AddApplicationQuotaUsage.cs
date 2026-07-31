using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationQuotaUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationQuotaUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    PeriodStartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedApplications = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationQuotaUsages", x => x.Id);
                    table.CheckConstraint("CK_ApplicationQuotaUsages_UsedApplications", "[UsedApplications] >= 0");
                    table.ForeignKey(
                        name: "FK_ApplicationQuotaUsages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationQuotaUsages_IsDeleted",
                table: "ApplicationQuotaUsages",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationQuotaUsages_UserId_Period_PeriodStartsAtUtc",
                table: "ApplicationQuotaUsages",
                columns: new[] { "UserId", "Period", "PeriodStartsAtUtc" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationQuotaUsages");
        }
    }
}
