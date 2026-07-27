using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.PublicJobs;

public sealed record PublicJobQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? CompanyId = null,
    Guid? CategoryId = null,
    EmploymentType? EmploymentType = null,
    WorkplaceType? WorkplaceType = null,
    ExperienceLevel? ExperienceLevel = null,
    string? Location = null,
    decimal? MinimumSalary = null,
    decimal? MaximumSalary = null,
    bool? IsFeatured = null,
    string SortBy = "publishedAt",
    string SortDirection = "desc");

public sealed record PublicJobSummary(
    Guid Id, string ReferenceNumber, string Title, string Slug,
    Guid CompanyId, string CompanyName, string CompanySlug, string? CompanyLogoUrl,
    Guid CategoryId, string CategoryName, string? Location,
    decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode,
    EmploymentType EmploymentType, WorkplaceType WorkplaceType,
    ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ExpiresAtUtc);

public sealed record PublicJobDetails(
    Guid Id, string ReferenceNumber, string Title, string Slug, string Description,
    string? Responsibilities, string? Requirements, string? Benefits,
    Guid CompanyId, string CompanyName, string CompanySlug, string? CompanyLogoUrl,
    string? CompanyDescription, string? CompanyWebsiteUrl,
    Guid CategoryId, string CategoryName, string? Location,
    decimal? MinimumSalary, decimal? MaximumSalary, string CurrencyCode,
    EmploymentType EmploymentType, WorkplaceType WorkplaceType,
    ExperienceLevel ExperienceLevel, bool IsFeatured,
    DateTime PublishedAtUtc, DateTime? ExpiresAtUtc);

public sealed record PopularCompanyResponse(
    Guid Id, string Name, string Slug, string? LogoUrl, string? Industry,
    string? Location, bool IsVerified, int ActiveJobCount);

public sealed record PublicJobPage(PagedResponse<PublicJobSummary> Page);
