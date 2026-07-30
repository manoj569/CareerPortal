using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCandidateEmailVerificationRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET
                    [EmailConfirmed] = CAST(1 AS bit),
                    [Status] = 2,
                    [EmailVerificationTokenHash] = NULL,
                    [EmailVerificationTokenExpiresAtUtc] = NULL,
                    [EmailVerificationSentAtUtc] = NULL
                WHERE [RoleId] = '3ec6976c-8752-48f5-a14f-1c81b6522c5d'
                  AND (
                      [EmailConfirmed] = CAST(0 AS bit)
                      OR [Status] <> 2
                      OR [EmailVerificationTokenHash] IS NOT NULL
                      OR [EmailVerificationTokenExpiresAtUtc] IS NOT NULL
                      OR [EmailVerificationSentAtUtc] IS NOT NULL
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Obsolete verification tokens and prior Candidate statuses cannot be
            // reconstructed safely. A rollback therefore leaves accounts usable.
        }
    }
}
