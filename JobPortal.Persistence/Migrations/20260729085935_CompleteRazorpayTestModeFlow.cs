using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteRazorpayTestModeFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderPaymentId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentHistory_ProviderEventId",
                table: "PaymentHistory");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReconciledAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProviderOrderCreatedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments",
                column: "ProviderOrderId",
                unique: true,
                filter: "[ProviderOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderPaymentId",
                table: "Payments",
                column: "ProviderPaymentId",
                unique: true,
                filter: "[ProviderPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_ProviderOrderCreatedAtUtc",
                table: "Payments",
                columns: new[] { "Status", "ProviderOrderCreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentHistory_ProviderEventId",
                table: "PaymentHistory",
                column: "ProviderEventId",
                unique: true,
                filter: "[ProviderEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderPaymentId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_ProviderOrderCreatedAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentHistory_ProviderEventId",
                table: "PaymentHistory");

            migrationBuilder.DropColumn(
                name: "LastReconciledAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderOrderCreatedAtUtc",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments",
                column: "ProviderOrderId",
                unique: true,
                filter: "[ProviderOrderId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderPaymentId",
                table: "Payments",
                column: "ProviderPaymentId",
                unique: true,
                filter: "[ProviderPaymentId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentHistory_ProviderEventId",
                table: "PaymentHistory",
                column: "ProviderEventId",
                unique: true,
                filter: "[ProviderEventId] IS NOT NULL AND [IsDeleted] = 0");
        }
    }
}
