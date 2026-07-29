using JobPortal.Application.Features.AdminApplications;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IAdminApplicationRepository
{
    Task<(IReadOnlyCollection<AdminApplicationListItem> Items, int TotalCount)> SearchAsync(
        AdminApplicationQuery query, CancellationToken cancellationToken = default);
    Task<JobApplication?> GetByIdAsync(
        Guid applicationId, CancellationToken cancellationToken = default);
}
