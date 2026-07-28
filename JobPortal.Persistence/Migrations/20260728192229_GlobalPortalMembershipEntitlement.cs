using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GlobalPortalMembershipEntitlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Memberships]
                    WHERE [IsDeleted] = 0
                    GROUP BY [UserId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51000, 'GlobalPortalMembershipEntitlement requires at most one non-deleted membership per user. Resolve duplicate company memberships before applying this migration.', 1;
                END
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Companies_CompanyId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_CompanyId",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_UserId_CompanyId",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Memberships");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships",
                column: "UserId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Memberships",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CompanyId",
                table: "Memberships",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId_CompanyId",
                table: "Memberships",
                columns: new[] { "UserId", "CompanyId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Companies_CompanyId",
                table: "Memberships",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
