using System.Linq.Expressions;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.PublicJobs;

public static class PublicJobProjections
{
    public static readonly Expression<Func<Job, PublicJobSummary>> Summary = job =>
        new PublicJobSummary(
            job.Id, job.ReferenceNumber, job.Title, job.Slug,
            job.CompanyId, job.Company.Name, job.Company.Slug, job.Company.LogoUrl,
            job.CategoryId, job.Category.Name, job.Location,
            job.MinimumSalary, job.MaximumSalary, job.CurrencyCode,
            job.EmploymentType, job.WorkplaceType, job.ExperienceLevel,
            job.IsFeatured, job.PublishedAtUtc!.Value, job.ExpiresAtUtc);
}
