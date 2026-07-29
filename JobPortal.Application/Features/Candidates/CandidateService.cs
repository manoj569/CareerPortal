using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Candidates;

public sealed class CandidateService(
    ICandidateRepository candidates,
    IDashboardRepository dashboard,
    IResumeStorage resumeStorage,
    IUnitOfWork unitOfWork,
    IValidator<UpdateCandidateProfileRequest> profileValidator,
    IValidator<CandidatePageQuery> pageValidator,
    IValidator<JobApplicationQuery> applicationQueryValidator,
    IValidator<CreateJobApplicationRequest> applicationValidator,
    TimeProvider timeProvider) : ICandidateService
{
    private const long MaximumResumeBytes = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string[]> AllowedResumeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".doc"] = ["application/msword"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]
    };

    public async Task<CandidateProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        MapProfile(await RequiredCandidateAsync(userId, cancellationToken));

    public async Task<CandidateProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateCandidateProfileRequest request, CancellationToken cancellationToken = default)
    {
        await profileValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        user.Headline = TextNormalizer.TrimOrNull(request.Headline);
        user.Bio = TextNormalizer.TrimOrNull(request.Bio);
        user.Location = TextNormalizer.TrimOrNull(request.Location);
        user.LinkedInUrl = TextNormalizer.TrimOrNull(request.LinkedInUrl);
        user.PortfolioUrl = TextNormalizer.TrimOrNull(request.PortfolioUrl);
        user.SkillsJson = SerializeStrings(request.Skills);
        user.EducationJson = SerializeStrings(request.Education);
        user.ExperienceJson = SerializeStrings(request.Experience);
        user.PreferredJobTypesJson = JsonSerializer.Serialize(request.PreferredJobTypes.Distinct());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapProfile(user);
    }

    public async Task<ResumeResponse> UploadResumeAsync(
        Guid userId, ResumeUpload upload, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var (extension, validatedContent) = await ValidateResumeAsync(upload, cancellationToken);
        var oldKey = user.ResumeStorageKey;
        await using var content = validatedContent;
        var storageKey = await resumeStorage.StoreAsync(content, extension, cancellationToken);
        user.ResumeStorageKey = storageKey;
        user.ResumeFileName = $"resume{extension}";
        user.ResumeContentType = upload.ContentType;
        user.ResumeSizeBytes = content.Length;
        user.ResumeUploadedAtUtc = UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteIfUnreferencedAsync(oldKey, cancellationToken);
        return MapResume(user)!;
    }

    public async Task<ResumeDownload> DownloadResumeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        if (user.ResumeStorageKey is null || user.ResumeFileName is null || user.ResumeContentType is null)
            throw new NotFoundException("Resume was not found.");
        var content = await resumeStorage.OpenReadAsync(user.ResumeStorageKey, cancellationToken)
            ?? throw new NotFoundException("Resume was not found.");
        return new(content, user.ResumeFileName, user.ResumeContentType);
    }

    public async Task DeleteResumeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var storageKey = user.ResumeStorageKey;
        if (storageKey is null) return;
        user.ResumeStorageKey = null;
        user.ResumeFileName = null;
        user.ResumeContentType = null;
        user.ResumeSizeBytes = null;
        user.ResumeUploadedAtUtc = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteIfUnreferencedAsync(storageKey, cancellationToken);
    }

    public async Task<PagedResponse<CandidateSavedJobResponse>> GetSavedJobsAsync(
        Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        await pageValidator.ValidateAndThrowAsync(query, cancellationToken);
        var dashboardQuery = new DashboardQuery(query.PageNumber, query.PageSize);
        var result = await dashboard.GetSavedJobsAsync(userId, dashboardQuery, cancellationToken);
        return new(result.Items.Select(x => new CandidateSavedJobResponse(
            x.SavedJobId, x.SavedAtUtc, x.Job.Id, x.Job.Title, x.Job.Slug, x.Job.CompanyName)).ToArray(),
            query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task SaveJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        if (!await dashboard.IsAvailableJobAsync(jobId, cancellationToken))
            throw new NotFoundException("Job was not found.");
        if (await dashboard.IsJobSavedAsync(userId, jobId, cancellationToken)) return;
        await dashboard.AddSavedJobAsync(new SavedJob { UserId = userId, JobId = jobId }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var saved = await dashboard.GetSavedJobAsync(userId, jobId, cancellationToken);
        if (saved is null) return;
        dashboard.RemoveSavedJob(saved);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<JobApplicationResponse> ApplyAsync(
        Guid userId, Guid jobId, CreateJobApplicationRequest request, CancellationToken cancellationToken = default)
    {
        await applicationValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var job = await candidates.GetAvailableJobAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Job was not found.");
        if (!await candidates.HasActiveMembershipAsync(userId, cancellationToken))
            throw new ConflictException("An active portal membership is required.");
        if (await candidates.HasApplicationAsync(userId, jobId, cancellationToken))
            throw new ConflictException("You have already applied to this job.");
        var application = new JobApplication
        {
            UserId = userId,
            JobId = jobId,
            Status = JobApplicationStatus.Submitted,
            CoverLetter = TextNormalizer.TrimOrNull(request.CoverLetter),
            ResumeStorageKey = user.ResumeStorageKey,
            ResumeFileName = user.ResumeFileName,
            ResumeContentType = user.ResumeContentType,
            SubmittedAtUtc = UtcNow
        };
        application.StatusHistory.Add(new JobApplicationStatusHistory
        {
            Application = application,
            ActorUserId = userId,
            PreviousStatus = null,
            NewStatus = JobApplicationStatus.Submitted,
            ChangedAtUtc = UtcNow
        });
        await candidates.AddApplicationAsync(application, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapApplication(application, job);
    }

    public async Task<PagedResponse<JobApplicationResponse>> GetApplicationsAsync(
        Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        await applicationQueryValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await candidates.GetApplicationsAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<JobApplicationResponse> GetApplicationAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var application = await candidates.GetApplicationAsync(userId, applicationId, cancellationToken)
            ?? throw new NotFoundException("Application was not found.");
        return MapApplication(application,
            new CandidateJob(application.JobId, application.Job.Title, application.Job.Slug, application.Job.Company.Name));
    }

    public async Task<JobApplicationResponse> WithdrawAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var application = await candidates.GetApplicationAsync(userId, applicationId, cancellationToken)
            ?? throw new NotFoundException("Application was not found.");
        if (application.Status != JobApplicationStatus.Submitted)
            throw new ConflictException("Only a Submitted application can be withdrawn.");
        var previous = application.Status;
        application.Status = JobApplicationStatus.Withdrawn;
        application.WithdrawnAtUtc = UtcNow;
        application.StatusHistory.Add(new JobApplicationStatusHistory
        {
            ApplicationId = application.Id,
            ActorUserId = userId,
            PreviousStatus = previous,
            NewStatus = JobApplicationStatus.Withdrawn,
            ChangedAtUtc = UtcNow
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapApplication(application,
            new CandidateJob(application.JobId, application.Job.Title, application.Job.Slug, application.Job.Company.Name));
    }

    private async Task<User> RequiredCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
        await candidates.GetCandidateAsync(userId, cancellationToken)
        ?? throw new UnauthorizedException("An active, verified Candidate account is required.");

    private static async Task<(string Extension, MemoryStream Content)> ValidateResumeAsync(
        ResumeUpload upload, CancellationToken cancellationToken)
    {
        if (upload.Length is <= 0 or > MaximumResumeBytes)
            throw new BadRequestException("Resume size must be between 1 byte and 5 MB.", "invalid_resume");
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (!AllowedResumeTypes.TryGetValue(extension, out var types) ||
            !types.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new BadRequestException("Resume must be a PDF, DOC, or DOCX file.", "invalid_resume");
        var content = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var readCount = await upload.Content.ReadAsync(buffer, cancellationToken);
            if (readCount == 0) break;
            if (content.Length + readCount > MaximumResumeBytes)
            {
                await content.DisposeAsync();
                throw new BadRequestException("Resume size must be between 1 byte and 5 MB.", "invalid_resume");
            }
            await content.WriteAsync(buffer.AsMemory(0, readCount), cancellationToken);
        }
        if (content.Length == 0)
        {
            await content.DisposeAsync();
            throw new BadRequestException("Resume size must be between 1 byte and 5 MB.", "invalid_resume");
        }
        var signature = content.GetBuffer().AsSpan(0, (int)Math.Min(8, content.Length));
        var valid = extension switch
        {
            ".pdf" => signature.Length >= 5 && signature[..5].SequenceEqual("%PDF-"u8),
            ".doc" => signature.Length >= 8 &&
                signature[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
            ".docx" => signature.Length >= 4 &&
                signature[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
            _ => false
        };
        if (!valid)
        {
            await content.DisposeAsync();
            throw new BadRequestException("Resume content does not match its file type.", "invalid_resume");
        }
        content.Position = 0;
        return (extension, content);
    }

    private static CandidateProfileResponse MapProfile(User user) => new(
        user.Id, user.Email, user.FirstName, user.LastName, user.Headline, user.Bio, user.Location,
        Deserialize<string>(user.SkillsJson), Deserialize<string>(user.EducationJson),
        Deserialize<string>(user.ExperienceJson), user.LinkedInUrl, user.PortfolioUrl,
        Deserialize<EmploymentType>(user.PreferredJobTypesJson), MapResume(user));
    private static ResumeResponse? MapResume(User user) =>
        user.ResumeFileName is not null && user.ResumeContentType is not null &&
        user.ResumeSizeBytes.HasValue && user.ResumeUploadedAtUtc.HasValue
            ? new(user.ResumeFileName, user.ResumeContentType, user.ResumeSizeBytes.Value, user.ResumeUploadedAtUtc.Value)
            : null;
    private static JobApplicationResponse MapApplication(JobApplication application, CandidateJob job) => new(
        application.Id, application.JobId, job.Title, job.Slug, job.CompanyName,
        application.Status, application.CoverLetter, application.ResumeFileName,
        application.SubmittedAtUtc, application.WithdrawnAtUtc);
    private static string SerializeStrings(IEnumerable<string> values) =>
        JsonSerializer.Serialize(values.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
    private static T[] Deserialize<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<T[]>(json) ?? [];
    private async Task DeleteIfUnreferencedAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (storageKey is not null &&
            !await candidates.IsResumeReferencedAsync(storageKey, cancellationToken))
            await resumeStorage.DeleteAsync(storageKey, cancellationToken);
    }
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
