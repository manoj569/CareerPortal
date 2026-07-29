using JobPortal.Application.Features.AdminApplications;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.AdminApplications;

public interface IAdminApplicationService
{
    Task<PagedResponse<AdminApplicationListItem>> SearchAsync(
        AdminApplicationQuery query, CancellationToken cancellationToken = default);
    Task<AdminApplicationDetail> GetAsync(
        Guid applicationId, CancellationToken cancellationToken = default);
    Task<AdminApplicationResumeDownload> DownloadResumeAsync(
        Guid applicationId, CancellationToken cancellationToken = default);
    Task<AdminApplicationDetail> UpdateStatusAsync(
        Guid administratorUserId, Guid applicationId, UpdateAdminApplicationStatusRequest request,
        CancellationToken cancellationToken = default);
}
