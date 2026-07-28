BEGIN TRANSACTION;
IF EXISTS (
    SELECT 1
    FROM [Memberships]
    WHERE [IsDeleted] = 0
    GROUP BY [UserId]
    HAVING COUNT(*) > 1)
BEGIN
    THROW 51000, 'GlobalPortalMembershipEntitlement requires at most one non-deleted membership per user. Resolve duplicate company memberships before applying this migration.', 1;
END

ALTER TABLE [Memberships] DROP CONSTRAINT [FK_Memberships_Companies_CompanyId];

DROP INDEX [IX_Memberships_CompanyId] ON [Memberships];

DROP INDEX [IX_Memberships_UserId_CompanyId] ON [Memberships];

DECLARE @var sysname;
SELECT @var = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Memberships]') AND [c].[name] = N'CompanyId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [Memberships] DROP CONSTRAINT [' + @var + '];');
ALTER TABLE [Memberships] DROP COLUMN [CompanyId];

CREATE UNIQUE INDEX [IX_Memberships_UserId] ON [Memberships] ([UserId]) WHERE [IsDeleted] = 0;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260728192229_GlobalPortalMembershipEntitlement', N'9.0.8');

COMMIT;
GO

