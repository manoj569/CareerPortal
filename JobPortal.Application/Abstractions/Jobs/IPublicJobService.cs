using JobPortal.Application.Features.PublicJobs;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Jobs;

public interface IPublicJobService
{
    Task<PagedResponse<PublicJobSummary>> SearchAsync(PublicJobQuery query, CancellationToken cancellationToken = default);
    Task<PublicJobDetails> GetDetailsAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PublicJobSummary>> GetRelatedAsync(string slug, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PopularCompanyResponse>> GetPopularCompaniesAsync(int limit, CancellationToken cancellationToken = default);
}
