using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecureMobileOtpAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PhoneConfirmed",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TermsAndPrivacyVersion",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [NormalizedEmail] = LOWER(LTRIM(RTRIM([Email])));

                UPDATE [Users]
                SET [PhoneConfirmed] = CAST(1 AS bit)
                WHERE [RoleId] = '3ec6976c-8752-48f5-a14f-1c81b6522c5d'
                  AND [NormalizedPhoneNumber] IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "PendingRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedPhoneNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    TermsAndPrivacyAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TermsAndPrivacyVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingRegistrations_Users_CompletedUserId",
                        column: x => x.CompletedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    NormalizedPhoneNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    OtpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: false),
                    SendCount = table.Column<int>(type: "int", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResetChallengeExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PendingRegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                    table.CheckConstraint("CK_OtpChallenges_FailedAttemptCount", "[FailedAttemptCount] BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_OtpChallenges_SendCount", "[SendCount] >= 1");
                    table.ForeignKey(
                        name: "FK_OtpChallenges_PendingRegistrations_PendingRegistrationId",
                        column: x => x.PendingRegistrationId,
                        principalTable: "PendingRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OtpChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_IsDeleted",
                table: "OtpChallenges",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_NormalizedPhoneNumber_Purpose_ConsumedAtUtc_ExpiresAtUtc",
                table: "OtpChallenges",
                columns: new[] { "NormalizedPhoneNumber", "Purpose", "ConsumedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_PendingRegistrationId",
                table: "OtpChallenges",
                column: "PendingRegistrationId",
                unique: true,
                filter: "[PendingRegistrationId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_Purpose_LastSentAtUtc",
                table: "OtpChallenges",
                columns: new[] { "Purpose", "LastSentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_UserId",
                table: "OtpChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_CompletedUserId",
                table: "PendingRegistrations",
                column: "CompletedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_ExpiresAtUtc_ClosedAtUtc",
                table: "PendingRegistrations",
                columns: new[] { "ExpiresAtUtc", "ClosedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_IsDeleted",
                table: "PendingRegistrations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_NormalizedEmail",
                table: "PendingRegistrations",
                column: "NormalizedEmail",
                unique: true,
                filter: "[ClosedAtUtc] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_NormalizedPhoneNumber",
                table: "PendingRegistrations",
                column: "NormalizedPhoneNumber",
                unique: true,
                filter: "[ClosedAtUtc] IS NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtpChallenges");

            migrationBuilder.DropTable(
                name: "PendingRegistrations");

            migrationBuilder.DropColumn(
                name: "PhoneConfirmed",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TermsAndPrivacyVersion",
                table: "Users");
        }
    }
}
