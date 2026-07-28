using JobPortal.Application.Features.AdminManagement;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.AdminManagement;

public interface ICompanyManagementService
{
    Task<PagedResponse<CompanyResponse>> SearchAsync(CompanySearchQuery query, CancellationToken cancellationToken = default);
    Task<CompanyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CompanyResponse> CreateAsync(Guid administratorUserId, CreateCompanyRequest request, CancellationToken cancellationToken = default);
    Task<CompanyResponse> UpdateAsync(Guid id, UpdateCompanyRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default);
}

public interface ICategoryManagementService
{
    Task<PagedResponse<CategoryResponse>> SearchAsync(CategorySearchQuery query, CancellationToken cancellationToken = default);
    Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default);
}
