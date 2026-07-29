using System.Globalization;
using System.Text;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Jobs;

public sealed class JobService(
    IJobRepository jobs,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreateJobRequest> createValidator,
    IValidator<UpdateJobRequest> updateValidator,
    IValidator<JobSearchQuery> searchValidator,
    TimeProvider timeProvider) : IJobService
{
    public async Task<JobResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ValidateReferencesAsync(request.CompanyId, request.CategoryId, cancellationToken);

        var id = Guid.NewGuid();
        var job = new Job
        {
            Id = id,
            ReferenceNumber = $"JOB-{UtcNow:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            Slug = $"{Slugify(request.Title)}-{id.ToString("N")[..8]}",
            Status = JobStatus.Draft
        };
        job.Apply(new UpdateJobRequest(request.Title, request.Description, request.CompanyId, request.CategoryId, request.ApplicationUrl,
            request.Responsibilities, request.Requirements, request.Benefits, request.Location,
            request.MinimumSalary, request.MaximumSalary, request.CurrencyCode, request.EmploymentType,
            request.WorkplaceType, request.ExperienceLevel, request.ExpiresAtUtc));

        await jobs.AddAsync(job, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Create,
            "Job",
            job.Id.ToString(),
            new Dictionary<string, string?> { ["status"] = JobStatus.Draft.ToString() }),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(job.Id, false, cancellationToken)).ToResponse();
    }

    public async Task<JobResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await RequiredJobAsync(id, true, cancellationToken)).ToResponse();

    public async Task<JobResponse> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ValidateReferencesAsync(request.CompanyId, request.CategoryId, cancellationToken);
        var job = await RequiredJobAsync(id, false, cancellationToken);
        if (job.Status == JobStatus.Archived)
            throw new ConflictException("An archived job cannot be updated.");
        if (job.Status == JobStatus.Published &&
            (!request.ExpiresAtUtc.HasValue || request.ExpiresAtUtc <= UtcNow))
            throw new BadRequestException(
                "A published job must have an expiration date in the future.");
        job.Apply(request);
        job.Slug = $"{Slugify(request.Title)}-{job.Id.ToString("N")[..8]}";
        jobs.Update(job);
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "Job",
            job.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["companyId"] = job.CompanyId.ToString(),
                ["categoryId"] = job.CategoryId.ToString(),
                ["status"] = job.Status.ToString()
            }), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(id, false, cancellationToken)).ToResponse();
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        jobs.Remove(job);
        await auditWriter.AppendAsync(new(
            AuditAction.Delete, "Job", job.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, true, cancellationToken);
        if (!job.IsDeleted)
            throw new ConflictException("A job must be soft-deleted before it can be permanently deleted.");
        await jobs.DeletePermanentlyAsync(id, cancellationToken);
    }

    public async Task<JobResponse> PublishAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        if (job.Status == JobStatus.Published)
            throw new ConflictException("The job is already published.");
        if (job.Status == JobStatus.Archived)
            throw new ConflictException("An archived job cannot be published.");

        await updateValidator.ValidateAndThrowAsync(ToUpdateRequest(job), cancellationToken);
        await ValidateReferencesAsync(job.CompanyId, job.CategoryId, cancellationToken);
        if (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc <= UtcNow)
            throw new BadRequestException(
                "A job must have an expiration date in the future before it can be published.");

        job.Status = JobStatus.Published;
        job.PublishedAtUtc = UtcNow;
        job.IsFeatured = false;
        job.IsHidden = false;
        return await SaveStateChangeAsync(job, AuditAction.Publish, cancellationToken);
    }

    public Task<JobResponse> UnpublishAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, AuditAction.Unpublish, job =>
        {
            if (job.Status != JobStatus.Published)
                throw new ConflictException("Only a published job can be unpublished.");
            job.Status = JobStatus.Draft;
            job.PublishedAtUtc = null;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> CloseAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, AuditAction.Close, job =>
        {
            if (job.Status != JobStatus.Published)
                throw new ConflictException("Only a published job can be closed.");
            job.Status = JobStatus.Closed;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, AuditAction.Archive, job =>
        {
            if (job.Status == JobStatus.Archived)
                throw new ConflictException("The job is already archived.");
            job.Status = JobStatus.Archived;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            id,
            isFeatured ? AuditAction.Feature : AuditAction.Unfeature,
            job =>
        {
            if (isFeatured && (job.Status != JobStatus.Published ||
                job.IsHidden || !job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc <= UtcNow))
                throw new ConflictException(
                    "Only visible, unexpired published jobs can be featured.");
            job.IsFeatured = isFeatured;
        }, cancellationToken);

    public Task<JobResponse> SetHiddenAsync(Guid id, bool isHidden, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, null, job =>
        {
            job.IsHidden = isHidden;
            if (isHidden)
                job.IsFeatured = false;
        }, cancellationToken);

    public async Task<PagedResponse<JobResponse>> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await jobs.SearchAsync(query, cancellationToken);
        return new PagedResponse<JobResponse>(result.Items.Select(x => x.ToResponse()).ToArray(),
            query.PageNumber, query.PageSize, result.TotalCount);
    }

    private async Task<JobResponse> ChangeAsync(
        Guid id,
        AuditAction? auditAction,
        Action<Job> change,
        CancellationToken cancellationToken)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        change(job);
        return await SaveStateChangeAsync(job, auditAction, cancellationToken);
    }

    private async Task<JobResponse> SaveStateChangeAsync(
        Job job,
        AuditAction? auditAction,
        CancellationToken cancellationToken)
    {
        jobs.Update(job);
        if (auditAction.HasValue)
        {
            await auditWriter.AppendAsync(new(
                auditAction.Value,
                "Job",
                job.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["status"] = job.Status.ToString(),
                    ["isFeatured"] = job.IsFeatured.ToString()
                }), cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(job.Id, false, cancellationToken)).ToResponse();
    }

    private async Task<Job> RequiredJobAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken) =>
        await jobs.GetByIdAsync(id, includeDeleted, cancellationToken)
        ?? throw new NotFoundException($"Job '{id}' was not found.");

    private async Task ValidateReferencesAsync(Guid companyId, Guid categoryId, CancellationToken cancellationToken)
    {
        if (!await jobs.CompanyExistsAsync(companyId, cancellationToken))
            throw new BadRequestException($"Company '{companyId}' does not exist.", "invalid_company");
        if (!await jobs.CategoryExistsAsync(categoryId, cancellationToken))
            throw new BadRequestException($"Category '{categoryId}' does not exist.", "invalid_category");
    }

    private static UpdateJobRequest ToUpdateRequest(Job job) => new(
        job.Title, job.Description, job.CompanyId, job.CategoryId, job.ApplicationUrl,
        job.Responsibilities, job.Requirements, job.Benefits, job.Location,
        job.MinimumSalary, job.MaximumSalary, job.CurrencyCode, job.EmploymentType,
        job.WorkplaceType, job.ExperienceLevel, job.ExpiresAtUtc);

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            var isAlphaNumeric = char.IsLetterOrDigit(character);
            if (isAlphaNumeric) { builder.Append(character); previousDash = false; }
            else if (!previousDash && builder.Length > 0) { builder.Append('-'); previousDash = true; }
        }
        var slug = builder.ToString().Trim('-');
        return slug[..Math.Min(slug.Length, 240)];
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

public sealed class JobExpiryService(
    IJobRepository jobs,
    TimeProvider timeProvider) : IJobExpiryService
{
    public Task<int> ExpireOverdueAsync(
        CancellationToken cancellationToken = default) =>
        jobs.ExpireOverduePublishedAsync(
            timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
}
