using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateRepositoryTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AvailableJobExcludesUnpublishedExpiredAndDeletedJobs()
    {
        await using var context = CreateContext();
        var company = new Company { Name = "Example", Slug = "example", OwnerUserId = Guid.NewGuid() };
        var available = CreateJob(company, JobStatus.Published, Now.AddDays(1));
        var unpublished = CreateJob(company, JobStatus.Draft, Now.AddDays(1));
        var expired = CreateJob(company, JobStatus.Published, Now.AddSeconds(-1));
        var deleted = CreateJob(company, JobStatus.Published, Now.AddDays(1));
        deleted.IsDeleted = true;
        context.AddRange(company, available, unpublished, expired, deleted);
        await context.SaveChangesAsync();
        var repository = new CandidateRepository(context, new FixedTimeProvider(Now));

        Assert.NotNull(await repository.GetAvailableJobAsync(available.Id));
        Assert.Null(await repository.GetAvailableJobAsync(unpublished.Id));
        Assert.Null(await repository.GetAvailableJobAsync(expired.Id));
        Assert.Null(await repository.GetAvailableJobAsync(deleted.Id));
    }

    [Fact]
    public async Task MembershipMustHaveStartedAndNotExpired()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        context.Memberships.Add(new Membership
        {
            UserId = userId,
            PlanName = "Portal",
            Status = MembershipStatus.Active,
            StartsAtUtc = Now.AddMinutes(1),
            EndsAtUtc = Now.AddDays(1)
        });
        await context.SaveChangesAsync();
        var repository = new CandidateRepository(context, new FixedTimeProvider(Now));

        Assert.False(await repository.HasActiveMembershipAsync(userId));
    }

    private static JobPortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Job CreateJob(Company company, JobStatus status, DateTime? expiresAtUtc) => new()
    {
        Company = company,
        CompanyId = company.Id,
        CategoryId = Guid.NewGuid(),
        Title = Guid.NewGuid().ToString(),
        Slug = Guid.NewGuid().ToString(),
        Description = "Description",
        ApplicationUrl = "https://example.test/apply",
        Status = status,
        ExpiresAtUtc = expiresAtUtc
    };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
