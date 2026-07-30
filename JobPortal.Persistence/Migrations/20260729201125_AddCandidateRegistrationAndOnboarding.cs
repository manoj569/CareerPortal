using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateRegistrationAndOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CareerStage",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "College",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Degree",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesiredOpportunitiesJson",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "GraduationYear",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhoneNumber",
                table: "Users",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingCompletedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAndPrivacyAcceptedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkPreferencesJson",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "YearsOfExperience",
                table: "Users",
                type: "decimal(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.Sql(
                """
                ;WITH CleanedPhoneNumbers AS
                (
                    SELECT
                        [Id],
                        [CreatedAtUtc],
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM([PhoneNumber])),
                            '+', ''),
                            ' ', ''),
                            '-', ''),
                            '(', ''),
                            ')', ''),
                            CHAR(9), '') AS [Digits]
                    FROM [Users]
                    WHERE [PhoneNumber] IS NOT NULL
                ),
                NationalNumbers AS
                (
                    SELECT
                        [Id],
                        [CreatedAtUtc],
                        CASE
                            WHEN LEN([Digits]) = 10 THEN [Digits]
                            WHEN LEN([Digits]) = 11 AND LEFT([Digits], 1) = '0'
                                THEN RIGHT([Digits], 10)
                            WHEN LEN([Digits]) = 12 AND LEFT([Digits], 2) = '91'
                                THEN RIGHT([Digits], 10)
                            ELSE NULL
                        END AS [NationalNumber]
                    FROM CleanedPhoneNumbers
                ),
                ValidNumbers AS
                (
                    SELECT
                        [Id],
                        [CreatedAtUtc],
                        '+91' + [NationalNumber] AS [NormalizedPhoneNumber]
                    FROM NationalNumbers
                    WHERE
                        LEN([NationalNumber]) = 10
                        AND [NationalNumber] NOT LIKE '%[^0-9]%'
                        AND LEFT([NationalNumber], 1) BETWEEN '6' AND '9'
                        AND [NationalNumber] <> REPLICATE(LEFT([NationalNumber], 1), 10)
                        AND LEFT([NationalNumber], 5) <> RIGHT([NationalNumber], 5)
                ),
                RankedNumbers AS
                (
                    SELECT
                        [Id],
                        [NormalizedPhoneNumber],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY [NormalizedPhoneNumber]
                            ORDER BY [CreatedAtUtc], [Id]
                        ) AS [DuplicateRank]
                    FROM ValidNumbers
                )
                UPDATE users
                SET
                    users.[PhoneNumber] = ranked.[NormalizedPhoneNumber],
                    users.[NormalizedPhoneNumber] = ranked.[NormalizedPhoneNumber]
                FROM [Users] AS users
                INNER JOIN RankedNumbers AS ranked ON users.[Id] = ranked.[Id]
                WHERE ranked.[DuplicateRank] = 1;

                UPDATE [Users]
                SET [PhoneNumber] = NULL
                WHERE
                    [PhoneNumber] IS NOT NULL
                    AND [NormalizedPhoneNumber] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedPhoneNumber",
                table: "Users",
                column: "NormalizedPhoneNumber",
                unique: true,
                filter: "[NormalizedPhoneNumber] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedPhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CareerStage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "College",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Degree",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DesiredOpportunitiesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GraduationYear",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedPhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TermsAndPrivacyAcceptedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WorkPreferencesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "YearsOfExperience",
                table: "Users");
        }
    }
}
