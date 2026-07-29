using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobPortal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministratorApplicationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResumeContentType",
                table: "JobApplications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobApplicationStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InternalNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobApplicationStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobApplicationStatusHistory_JobApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobApplicationStatusHistory_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                UPDATE [JobApplications]
                SET [ResumeContentType] = CASE
                    WHEN LOWER([ResumeFileName]) LIKE '%.pdf' THEN 'application/pdf'
                    WHEN LOWER([ResumeFileName]) LIKE '%.doc' THEN 'application/msword'
                    WHEN LOWER([ResumeFileName]) LIKE '%.docx'
                        THEN 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
                    ELSE NULL
                END
                WHERE [ResumeStorageKey] IS NOT NULL;

                INSERT INTO [JobApplicationStatusHistory]
                    ([Id], [PreviousStatus], [NewStatus], [ChangedAtUtc], [InternalNote],
                     [ApplicationId], [ActorUserId], [CreatedAtUtc], [UpdatedAtUtc],
                     [IsDeleted], [DeletedAtUtc])
                SELECT NEWID(), NULL, 1, [SubmittedAtUtc], NULL,
                       [Id], [UserId], [SubmittedAtUtc], NULL, 0, NULL
                FROM [JobApplications]
                WHERE [IsDeleted] = 0;

                INSERT INTO [JobApplicationStatusHistory]
                    ([Id], [PreviousStatus], [NewStatus], [ChangedAtUtc], [InternalNote],
                     [ApplicationId], [ActorUserId], [CreatedAtUtc], [UpdatedAtUtc],
                     [IsDeleted], [DeletedAtUtc])
                SELECT NEWID(), 1, 5, COALESCE([WithdrawnAtUtc], [UpdatedAtUtc], [SubmittedAtUtc]),
                       NULL, [Id], [UserId],
                       COALESCE([WithdrawnAtUtc], [UpdatedAtUtc], [SubmittedAtUtc]),
                       NULL, 0, NULL
                FROM [JobApplications]
                WHERE [IsDeleted] = 0 AND [Status] = 5;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_JobApplicationStatusHistory_ActorUserId_ChangedAtUtc",
                table: "JobApplicationStatusHistory",
                columns: new[] { "ActorUserId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplicationStatusHistory_ApplicationId_ChangedAtUtc",
                table: "JobApplicationStatusHistory",
                columns: new[] { "ApplicationId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplicationStatusHistory_IsDeleted",
                table: "JobApplicationStatusHistory",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobApplicationStatusHistory");

            migrationBuilder.DropColumn(
                name: "ResumeContentType",
                table: "JobApplications");
        }
    }
}
