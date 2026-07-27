using JobPortal.Application.Features.Memberships;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Memberships;

public interface IMembershipService
{
    Task<ApplicationAccessResponse> GetApplicationAccessAsync(Guid? userId, string jobSlug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MembershipResponse>> GetMyMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResponse<MembershipHistoryResponse>> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
}
