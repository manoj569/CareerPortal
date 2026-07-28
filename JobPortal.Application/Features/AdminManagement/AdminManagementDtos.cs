using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.AdminManagement;

public sealed record AdminOptionResponse(Guid Id, string Name, string Slug);

public sealed record CompanySearchQuery(
    int PageNumber = 1, int PageSize = 20, string? Search = null,
    bool? IsVerified = null, bool? IsDeleted = false,
    string SortBy = "createdAt", string SortDirection = "desc");
public sealed record CreateCompanyRequest(
    string Name, string? Slug, string? Description, string? WebsiteUrl,
    string? LogoUrl, string? Industry, string? Location, int? EmployeeCount, bool IsVerified);
public sealed record UpdateCompanyRequest(
    string Name, string? Slug, string? Description, string? WebsiteUrl,
    string? LogoUrl, string? Industry, string? Location, int? EmployeeCount, bool IsVerified);
public sealed record CompanyResponse(
    Guid Id, string Name, string Slug, string? Description, string? WebsiteUrl,
    string? LogoUrl, string? Industry, string? Location, int? EmployeeCount,
    bool IsVerified, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool IsDeleted);

public sealed record CategorySearchQuery(
    int PageNumber = 1, int PageSize = 20, string? Search = null,
    Guid? ParentCategoryId = null, bool RootOnly = false, bool? IsDeleted = false,
    string SortBy = "displayOrder", string SortDirection = "asc");
public sealed record CreateCategoryRequest(
    string Name, string? Slug, string? Description, int DisplayOrder, Guid? ParentCategoryId);
public sealed record UpdateCategoryRequest(
    string Name, string? Slug, string? Description, int DisplayOrder, Guid? ParentCategoryId);
public sealed record CategoryResponse(
    Guid Id, string Name, string Slug, string? Description, int DisplayOrder,
    Guid? ParentCategoryId, string? ParentCategoryName,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool IsDeleted);
