using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class PublicJobRepository(
    JobPortalDbContext context,
    TimeProvider timeProvider) : IPublicJobRepository
{
    public async Task<(IReadOnlyCollection<PublicJobSummary> Items, int TotalCount)> SearchAsync(
        PublicJobQuery query, CancellationToken cancellationToken = default)
    {
        var source = AvailableJobs();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x =>
                x.Title.Contains(term) || x.Description.Contains(term) ||
                x.Company.Name.Contains(term) || x.Category.Name.Contains(term) ||
                (x.Location != null && x.Location.Contains(term)));
        }
        if (query.CompanyId.HasValue) source = source.Where(x => x.CompanyId == query.CompanyId);
        if (query.CategoryId.HasValue) source = source.Where(x => x.CategoryId == query.CategoryId);
        if (query.EmploymentType.HasValue) source = source.Where(x => x.EmploymentType == query.EmploymentType);
        if (query.WorkplaceType.HasValue) source = source.Where(x => x.WorkplaceType == query.WorkplaceType);
        if (query.ExperienceLevel.HasValue) source = source.Where(x => x.ExperienceLevel == query.ExperienceLevel);
        if (query.IsFeatured.HasValue) source = source.Where(x => x.IsFeatured == query.IsFeatured);
        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim();
            source = source.Where(x => x.Location != null && x.Location.Contains(location));
        }
        if (query.MinimumSalary.HasValue)
            source = source.Where(x => x.MaximumSalary == null || x.MaximumSalary >= query.MinimumSalary);
        if (query.MaximumSalary.HasValue)
            source = source.Where(x => x.MinimumSalary == null || x.MinimumSalary <= query.MaximumSalary);

        var totalCount = await source.CountAsync(cancellationToken);
        var items = await ApplySorting(source, query.SortBy,
                query.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(PublicJobProjections.Summary)
            .ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<PublicJobDetails?> GetDetailsAsync(string slug, CancellationToken cancellationToken = default) =>
        AvailableJobs()
            .Where(x => x.Slug == slug)
            .Select(job => new PublicJobDetails(
                job.Id, job.ReferenceNumber, job.Title, job.Slug, job.Description,
                job.Responsibilities, job.Requirements, job.Benefits,
                job.CompanyId, job.Company.Name, job.Company.Slug, job.Company.LogoUrl,
                job.Company.Description, job.Company.WebsiteUrl,
                job.CategoryId, job.Category.Name, job.Location,
                job.MinimumSalary, job.MaximumSalary, job.CurrencyCode,
                job.EmploymentType, job.WorkplaceType, job.ExperienceLevel,
                job.IsFeatured, job.PublishedAtUtc!.Value, job.ExpiresAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PublicJobSummary>> GetRelatedAsync(
        string slug, int limit, CancellationToken cancellationToken = default)
    {
        var target = await AvailableJobs().Where(x => x.Slug == slug)
            .Select(x => new { x.Id, x.CategoryId, x.CompanyId, x.EmploymentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null) return Array.Empty<PublicJobSummary>();

        return await AvailableJobs()
            .Where(x => x.Id != target.Id &&
                (x.CategoryId == target.CategoryId || x.CompanyId == target.CompanyId ||
                 x.EmploymentType == target.EmploymentType))
            .OrderByDescending(x => x.CategoryId == target.CategoryId)
            .ThenByDescending(x => x.CompanyId == target.CompanyId)
            .ThenByDescending(x => x.IsFeatured)
            .ThenByDescending(x => x.PublishedAtUtc)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(PublicJobProjections.Summary)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<PopularCompanyResponse>> GetPopularCompaniesAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return await context.Companies.AsNoTracking()
            .Select(company => new PopularCompanyResponse(
                company.Id, company.Name, company.Slug, company.LogoUrl,
                company.Industry, company.Location, company.IsVerified,
                company.Jobs.Count(job => job.Status == JobStatus.Published &&
                    !job.IsHidden && !job.IsDeleted &&
                    (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc > utcNow))))
            .Where(company => company.ActiveJobCount > 0)
            .OrderByDescending(company => company.ActiveJobCount)
            .ThenByDescending(company => company.IsVerified)
            .ThenBy(company => company.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    private IQueryable<Job> AvailableJobs()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return context.Jobs.AsNoTracking().Where(job =>
            job.Status == JobStatus.Published && !job.IsHidden &&
            job.PublishedAtUtc.HasValue &&
            (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc > utcNow));
    }

    private static IQueryable<Job> ApplySorting(IQueryable<Job> source, string sortBy, bool descending) =>
        (sortBy.ToLowerInvariant(), descending) switch
        {
            ("title", false) => source.OrderBy(x => x.Title).ThenBy(x => x.Id),
            ("title", true) => source.OrderByDescending(x => x.Title).ThenBy(x => x.Id),
            ("createdat", false) => source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            ("createdat", true) => source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            ("minimumsalary", false) => source.OrderBy(x => x.MinimumSalary).ThenBy(x => x.Id),
            ("minimumsalary", true) => source.OrderByDescending(x => x.MinimumSalary).ThenBy(x => x.Id),
            ("maximumsalary", false) => source.OrderBy(x => x.MaximumSalary).ThenBy(x => x.Id),
            ("maximumsalary", true) => source.OrderByDescending(x => x.MaximumSalary).ThenBy(x => x.Id),
            ("expiresat", false) => source.OrderBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id),
            ("expiresat", true) => source.OrderByDescending(x => x.ExpiresAtUtc).ThenBy(x => x.Id),
            (_, false) => source.OrderBy(x => x.PublishedAtUtc).ThenBy(x => x.Id),
            _ => source.OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id)
        };
}
