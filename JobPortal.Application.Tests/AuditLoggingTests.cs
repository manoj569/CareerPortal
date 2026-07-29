using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Auditing;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AuditLoggingTests
{
    [Fact]
    public async Task WriterCapturesActorCorrelationAndOnlyAllowlistedMetadata()
    {
        var actorId = Guid.NewGuid();
        var repository = new AuditRepositoryFake();
        var writer = new AuditWriter(
            repository,
            new AuditContextFake(actorId, "Candidate", "corr-42"));

        await writer.AppendAsync(new(
            AuditAction.Confirm,
            "Payment",
            Guid.NewGuid().ToString(),
            new Dictionary<string, string?>
            {
                ["status"] = "Paid",
                ["password"] = "password-secret",
                ["jwt"] = "jwt-secret",
                ["refreshToken"] = "refresh-secret",
                ["razorpaySignature"] = "signature-secret",
                ["resetToken"] = "reset-secret",
                ["rawBody"] = "raw-body-secret",
                ["resumeContent"] = "resume-secret",
                ["email"] = "candidate@example.test"
            }));

        var log = Assert.Single(repository.Added);
        Assert.Equal(actorId, log.UserId);
        Assert.Equal("Candidate", log.ActorRole);
        Assert.Equal("corr-42", log.CorrelationId);
        Assert.Equal(AuditAction.Confirm, log.Action);
        Assert.Equal("Payment", log.EntityName);
        Assert.Contains("\"status\":\"Paid\"", log.ChangesJson, StringComparison.Ordinal);
        foreach (var secret in new[]
                 {
                     "password-secret", "jwt-secret", "refresh-secret",
                     "signature-secret", "reset-secret", "raw-body-secret",
                     "resume-secret", "candidate@example.test"
                 })
            Assert.DoesNotContain(secret, log.ChangesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SafeDtoDropsSensitiveLegacyMetadata()
    {
        var log = NewLog(AuditAction.Update, "CandidateProfile", "candidate-1", "corr-1");
        log.ChangesJson =
            """{"status":"Active","password":"legacy-secret","rawBody":"private"}""";
        var repository = new AuditRepositoryFake
        {
            SearchResult = ([log], 1)
        };
        var service = new AuditLogService(repository, new AuditLogQueryValidator());

        var response = await service.SearchAsync(new());

        var item = Assert.Single(response.Items);
        Assert.Equal("Active", item.Metadata["status"]);
        Assert.DoesNotContain("password", item.Metadata.Keys);
        Assert.DoesNotContain("rawBody", item.Metadata.Keys);
        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("legacy-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepositoryAppliesFiltersAndPagination()
    {
        await using var context = CreateContext();
        var actorId = Guid.NewGuid();
        context.AuditLogs.AddRange(
            NewLog(AuditAction.Update, "Job", "job-1", "corr-match", actorId),
            NewLog(AuditAction.Update, "Job", "job-2", "corr-other", actorId),
            NewLog(AuditAction.Create, "Company", "company-1", "corr-match"));
        await context.SaveChangesAsync();
        var repository = new AuditLogRepository(context);
        var now = DateTime.UtcNow;

        var filtered = await repository.SearchAsync(new(
            PageNumber: 1,
            PageSize: 20,
            ActorId: actorId,
            Action: AuditAction.Update,
            EntityType: "Job",
            EntityId: "job-1",
            FromUtc: now.AddMinutes(-1),
            ToUtc: now.AddMinutes(1),
            CorrelationId: "corr-match"));
        var paged = await repository.SearchAsync(new(
            PageNumber: 2,
            PageSize: 1,
            ActorId: actorId,
            Action: AuditAction.Update,
            EntityType: "Job"));

        Assert.Equal("job-1", Assert.Single(filtered.Items).EntityId);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Single(paged.Items);
        Assert.Equal(2, paged.TotalCount);
    }

    [Fact]
    public async Task DbContextRejectsAuditUpdateAndDelete()
    {
        await using var context = CreateContext();
        var log = NewLog(AuditAction.Create, "Company", "company-1", "corr-1");
        context.AuditLogs.Add(log);
        await context.SaveChangesAsync();

        log.EntityId = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        context.Entry(log).State = EntityState.Unchanged;
        context.AuditLogs.Remove(log);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public void AdminApiIsExactRoleProtectedAndNoMutationContractExists()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            root, "JobPortal.API", "Controllers", "AdminAuditLogsController.cs"));
        var repositoryMethods = typeof(IAuditLogRepository)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(
            "[Authorize(Roles = \"Administrator\")]",
            controller,
            StringComparison.Ordinal);
        Assert.Contains("[HttpGet]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPut", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpPatch", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[HttpDelete", controller, StringComparison.Ordinal);
        Assert.Equal(["AddAsync", "SearchAsync"], repositoryMethods.Order().ToArray());
    }

    [Fact]
    public void MigrationAddsDatabaseAppendOnlyTriggerWithoutHistoricalBackfill()
    {
        var migrations = Path.Combine(FindRepositoryRoot(), "JobPortal.Persistence", "Migrations");
        var migrationPath = Directory
            .GetFiles(migrations, "*_AddSecureAppendOnlyAuditLogging.cs")
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("TR_AuditLogs_AppendOnly", migration, StringComparison.Ordinal);
        Assert.Contains("INSTEAD OF UPDATE, DELETE", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateData(", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("InsertData(", migration, StringComparison.Ordinal);
    }

    private static AuditLog NewLog(
        AuditAction action,
        string entityType,
        string entityId,
        string correlationId,
        Guid? actorId = null) =>
        new()
        {
            Action = action,
            EntityName = entityType,
            EntityId = entityId,
            CorrelationId = correlationId,
            ActorRole = actorId.HasValue ? "Administrator" : "System",
            UserId = actorId
        };

    private static JobPortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class AuditContextFake(
        Guid? actorUserId,
        string? actorRole,
        string? correlationId) : IAuditContextAccessor
    {
        public Guid? ActorUserId { get; } = actorUserId;
        public string? ActorRole { get; } = actorRole;
        public string? CorrelationId { get; } = correlationId;
    }

    private sealed class AuditRepositoryFake : IAuditLogRepository
    {
        public List<AuditLog> Added { get; } = [];
        public (IReadOnlyCollection<AuditLog> Items, int TotalCount) SearchResult { get; init; }
            = ([], 0);

        public Task AddAsync(
            AuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Added.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyCollection<AuditLog> Items, int TotalCount)> SearchAsync(
            AuditLogQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(SearchResult);
    }
}
