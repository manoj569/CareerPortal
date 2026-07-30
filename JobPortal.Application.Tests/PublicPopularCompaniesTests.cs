using JobPortal.API.Controllers;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PublicPopularCompaniesTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GetApiJobsCompaniesPopularQueryIsSqlServerTranslatable()
    {
        using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseSqlServer(
                    "Server=(localdb)\\MSSQLLocalDB;Database=TranslationOnly;Trusted_Connection=True")
                .Options);
        var repository = new PublicJobRepository(
            context, new FixedTimeProvider(Now));

        var sql = repository.PopularCompaniesQuery(10).ToQueryString();

        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExpiresAtUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetApiJobsCompaniesPopularPreservesVisibilityAndOrdering()
    {
        await using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var alpha = Company("Alpha", true);
        var bravo = Company("Bravo", false);
        var charlie = Company("Charlie", true);
        var unavailable = Company("Unavailable", true);
        var deleted = Company("Deleted", true);
        deleted.IsDeleted = true;
        context.Companies.AddRange(alpha, bravo, charlie, unavailable, deleted);
        context.Jobs.AddRange(
            AvailableJob(alpha, "alpha-1"),
            AvailableJob(alpha, "alpha-2"),
            AvailableJob(bravo, "bravo-1"),
            AvailableJob(charlie, "charlie-1"),
            Job(unavailable, "hidden", JobStatus.Published, true, Now.AddDays(2)),
            Job(unavailable, "expired", JobStatus.Published, false, Now),
            Job(unavailable, "closed", JobStatus.Closed, false, Now.AddDays(2)),
            Job(unavailable, "deleted-job", JobStatus.Published, false, Now.AddDays(2), true),
            AvailableJob(deleted, "deleted-company"));
        await context.SaveChangesAsync();
        var repository = new PublicJobRepository(
            context, new FixedTimeProvider(Now));
        var service = new PublicJobService(
            repository, new PublicJobQueryValidator());
        var controller = new PublicJobsController(service);

        var action = await controller.PopularCompanies(10);
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<
            ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>>(
            response.Value);
        var result = envelope.Data;

        Assert.Collection(
            result,
            company =>
            {
                Assert.Equal(alpha.Id, company.Id);
                Assert.Equal(2, company.ActiveJobCount);
            },
            company =>
            {
                Assert.Equal(charlie.Id, company.Id);
                Assert.Equal(1, company.ActiveJobCount);
            },
            company =>
            {
                Assert.Equal(bravo.Id, company.Id);
                Assert.Equal(1, company.ActiveJobCount);
            });
    }

    private static Company Company(string name, bool isVerified) =>
        new()
        {
            Name = name,
            Slug = name.ToLowerInvariant(),
            IsVerified = isVerified,
            OwnerUserId = Guid.NewGuid()
        };

    private static Job AvailableJob(Company company, string slug) =>
        Job(company, slug, JobStatus.Published, false, Now.AddDays(2));

    private static Job Job(
        Company company,
        string slug,
        JobStatus status,
        bool isHidden,
        DateTime? expiresAtUtc,
        bool isDeleted = false) =>
        new()
        {
            ReferenceNumber = $"REF-{slug}",
            Title = slug,
            Slug = slug,
            Description = "Description",
            ApplicationUrl = "https://example.test/apply",
            Status = status,
            IsHidden = isHidden,
            IsDeleted = isDeleted,
            PublishedAtUtc = Now.AddDays(-1),
            ExpiresAtUtc = expiresAtUtc,
            CompanyId = company.Id,
            Company = company,
            CategoryId = Guid.NewGuid()
        };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
