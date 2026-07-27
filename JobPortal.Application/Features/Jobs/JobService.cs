using System.Globalization;
using System.Text;
using FluentValidation;
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
        job.Apply(request);
        job.Slug = $"{Slugify(request.Title)}-{job.Id.ToString("N")[..8]}";
        jobs.Update(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(id, false, cancellationToken)).ToResponse();
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        jobs.Remove(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, true, cancellationToken);
        if (!job.IsDeleted)
            throw new ConflictException("A job must be soft-deleted before it can be permanently deleted.");
        await jobs.DeletePermanentlyAsync(id, cancellationToken);
    }

    public Task<JobResponse> PublishAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, job =>
        {
            if (job.Status == JobStatus.Published)
                throw new ConflictException("The job is already published.");
            if (job.Status == JobStatus.Archived)
                throw new ConflictException("An archived job cannot be published.");
            if (job.ExpiresAtUtc <= UtcNow)
                throw new BadRequestException("A job with an expired expiration date cannot be published.");
            job.Status = JobStatus.Published;
            job.PublishedAtUtc = UtcNow;
            job.IsHidden = false;
        }, cancellationToken);

    public Task<JobResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, job =>
        {
            if (job.Status == JobStatus.Archived)
                throw new ConflictException("The job is already archived.");
            job.Status = JobStatus.Archived;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, job =>
        {
            if (isFeatured && job.Status != JobStatus.Published)
                throw new ConflictException("Only published jobs can be featured.");
            job.IsFeatured = isFeatured;
        }, cancellationToken);

    public Task<JobResponse> SetHiddenAsync(Guid id, bool isHidden, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, job => job.IsHidden = isHidden, cancellationToken);

    public async Task<PagedResponse<JobResponse>> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await jobs.SearchAsync(query, cancellationToken);
        return new PagedResponse<JobResponse>(result.Items.Select(x => x.ToResponse()).ToArray(),
            query.PageNumber, query.PageSize, result.TotalCount);
    }

    private async Task<JobResponse> ChangeAsync(Guid id, Action<Job> change, CancellationToken cancellationToken)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        change(job);
        jobs.Update(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(id, false, cancellationToken)).ToResponse();
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
