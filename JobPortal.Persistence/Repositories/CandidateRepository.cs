using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CandidateRepository(
    JobPortalDbContext context,
    TimeProvider timeProvider) : ICandidateRepository
{
    public Task<User?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.SingleOrDefaultAsync(x => x.Id == userId &&
            x.RoleId == SystemRoleIds.Candidate && x.EmailConfirmed &&
            x.Status == UserStatus.Active, cancellationToken);

    public Task<CandidateJob?> GetAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return context.Jobs.AsNoTracking().Where(x => x.Id == jobId &&
                x.Status == JobStatus.Published && !x.IsHidden &&
                x.PublishedAtUtc.HasValue &&
                x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc > now)
            .Select(x => new CandidateJob(x.Id, x.Title, x.Slug, x.Company.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return context.Memberships.AsNoTracking().AnyAsync(x => x.UserId == userId &&
            x.Status == MembershipStatus.Active &&
            x.StartsAtUtc <= now &&
            (!x.EndsAtUtc.HasValue || x.EndsAtUtc > now), cancellationToken);
    }

    public Task<bool> IsResumeReferencedAsync(
        string storageKey, CancellationToken cancellationToken = default) =>
        context.JobApplications.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.ResumeStorageKey == storageKey, cancellationToken);

    public Task<JobApplication?> GetApplicationAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default) =>
        context.JobApplications.Include(x => x.Job).ThenInclude(x => x.Company)
            .SingleOrDefaultAsync(x => x.Id == applicationId && x.UserId == userId, cancellationToken);

    public Task<bool> HasApplicationAsync(
        Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
        context.JobApplications.IgnoreQueryFilters()
            .AnyAsync(x => x.UserId == userId && x.JobId == jobId, cancellationToken);

    public Task AddApplicationAsync(JobApplication application, CancellationToken cancellationToken = default) =>
        context.JobApplications.AddAsync(application, cancellationToken).AsTask();

    public async Task<(IReadOnlyCollection<JobApplicationResponse> Items, int TotalCount)> GetApplicationsAsync(
        Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.JobApplications.AsNoTracking().Where(x => x.UserId == userId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new JobApplicationResponse(
                x.Id, x.JobId, x.Job.Title, x.Job.Slug, x.Job.Company.Name, x.Status,
                x.CoverLetter, x.ResumeFileName, x.SubmittedAtUtc, x.WithdrawnAtUtc))
            .ToArrayAsync(cancellationToken);
        return (items, count);
    }
}
