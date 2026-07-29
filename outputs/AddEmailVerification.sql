BEGIN TRANSACTION;
UPDATE [Users]
SET [EmailConfirmed] = 1
WHERE [Status] = 2 AND [EmailConfirmed] = 0;

ALTER TABLE [Users] ADD [EmailVerificationSentAtUtc] datetime2 NULL;

ALTER TABLE [Users] ADD [EmailVerificationTokenExpiresAtUtc] datetime2 NULL;

ALTER TABLE [Users] ADD [EmailVerificationTokenHash] nvarchar(64) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260729054014_AddEmailVerification', N'9.0.8');

COMMIT;
GO

