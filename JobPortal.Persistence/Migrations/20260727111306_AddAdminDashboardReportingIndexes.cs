using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDashboardReportingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedAtUtc",
                table: "Users",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CreatedAtUtc",
                table: "Payments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_PaidAtUtc_CurrencyCode",
                table: "Payments",
                columns: new[] { "Status", "PaidAtUtc", "CurrencyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CreatedAtUtc",
                table: "Jobs",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_CreatedAtUtc",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CreatedAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_PaidAtUtc_CurrencyCode",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_CreatedAtUtc",
                table: "Jobs");
        }
    }
}
