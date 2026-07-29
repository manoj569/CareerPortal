using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default);
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<int> ExpireOverduePublishedAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    void Update(Job job);
    void Remove(Job job);
    Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default);
}
