using System.Text.Json;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateModuleTests
{
    [Fact]
    public async Task ProfileRequiresOwnedActiveVerifiedCandidate()
    {
        var fixture = CreateFixture();
        fixture.Repository.Candidate = null;

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => fixture.Service.GetProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ApplicationRequiresMembershipAndAvailableJob()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = false;
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.ApplyAsync(fixture.Candidate.Id, fixture.Job.Id, new(null)));

        fixture.Repository.HasMembership = true;
        fixture.Repository.AvailableJob = null;
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.ApplyAsync(fixture.Candidate.Id, fixture.Job.Id, new(null)));
    }

    [Fact]
    public async Task DuplicateApplicationIsRejected()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasPriorApplication = true;

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.ApplyAsync(fixture.Candidate.Id, fixture.Job.Id, new("Interested")));
        Assert.Empty(fixture.Repository.AddedApplications);
    }

    [Fact]
    public async Task ApplicationUsesCurrentResumeAndCannotReadAnotherCandidatesApplication()
    {
        var fixture = CreateFixture();
        fixture.Candidate.ResumeStorageKey = "current.pdf";
        fixture.Candidate.ResumeFileName = "resume.pdf";

        var submitted = await fixture.Service.ApplyAsync(
            fixture.Candidate.Id, fixture.Job.Id, new("Interested"));

        Assert.Equal("resume.pdf", submitted.ResumeFileName);
        Assert.Equal("current.pdf", fixture.Repository.AddedApplications.Single().ResumeStorageKey);
        Assert.Contains(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Submit &&
                audit.EntityId == fixture.Repository.AddedApplications.Single().Id.ToString());
        var history = Assert.Single(fixture.Repository.AddedApplications.Single().StatusHistory);
        Assert.Null(history.PreviousStatus);
        Assert.Equal(JobApplicationStatus.Submitted, history.NewStatus);
        Assert.Equal(fixture.Candidate.Id, history.ActorUserId);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetApplicationAsync(fixture.Candidate.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task OnlySubmittedApplicationCanBeWithdrawn()
    {
        var fixture = CreateFixture();
        var submitted = fixture.CreateApplication(JobApplicationStatus.Submitted);
        fixture.Repository.OwnedApplication = submitted;

        var response = await fixture.Service.WithdrawAsync(fixture.Candidate.Id, submitted.Id);

        Assert.Equal(JobApplicationStatus.Withdrawn, response.Status);
        Assert.NotNull(submitted.WithdrawnAtUtc);
        var history = Assert.Single(submitted.StatusHistory);
        Assert.Equal(JobApplicationStatus.Submitted, history.PreviousStatus);
        Assert.Equal(JobApplicationStatus.Withdrawn, history.NewStatus);
        Assert.Equal(fixture.Candidate.Id, history.ActorUserId);

        fixture.Repository.OwnedApplication = fixture.CreateApplication(JobApplicationStatus.Reviewed);
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.WithdrawAsync(fixture.Candidate.Id, fixture.Repository.OwnedApplication.Id));
    }

    [Theory]
    [InlineData("resume.exe", "application/pdf")]
    [InlineData("resume.pdf", "text/plain")]
    [InlineData("resume.pdf", "application/pdf")]
    public async Task ResumeRejectsInvalidExtensionContentTypeOrSignature(string name, string contentType)
    {
        var fixture = CreateFixture();
        await using var stream = new MemoryStream("not a resume"u8.ToArray());

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.UploadResumeAsync(
            fixture.Candidate.Id, new(stream, stream.Length, name, contentType)));
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task ResumeReplacementUsesServerKeyAndPreservesApplicationSnapshot()
    {
        var fixture = CreateFixture();
        fixture.Candidate.ResumeStorageKey = "prior.pdf";
        fixture.Repository.ReferencedResumeKeys.Add("prior.pdf");
        await using var stream = new MemoryStream("%PDF-1.7 test"u8.ToArray());

        var response = await fixture.Service.UploadResumeAsync(fixture.Candidate.Id,
            new(stream, stream.Length, "../../unsafe.pdf", "application/pdf"));

        Assert.Equal("resume.pdf", response.FileName);
        Assert.Single(fixture.Storage.Stored);
        Assert.DoesNotContain("prior.pdf", fixture.Storage.Deleted);
        Assert.DoesNotContain("unsafe", fixture.Storage.Stored.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Upload &&
                !JsonSerializer.Serialize(audit).Contains(
                    "unsafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SavingAJobIsIdempotentAndUnavailableJobsAreRejected()
    {
        var fixture = CreateFixture();
        fixture.Dashboard.AlreadySaved = true;
        await fixture.Service.SaveJobAsync(fixture.Candidate.Id, fixture.Job.Id);
        Assert.Empty(fixture.Dashboard.Added);

        fixture.Dashboard.AlreadySaved = false;
        fixture.Dashboard.JobAvailable = false;
        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.SaveJobAsync(fixture.Candidate.Id, fixture.Job.Id));
    }

    [Fact]
    public async Task ApplicationListEnforcesPaginationAndPassesOwnerScope()
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAnyAsync<FluentValidation.ValidationException>(() =>
            fixture.Service.GetApplicationsAsync(fixture.Candidate.Id, new(0, 20)));

        await fixture.Service.GetApplicationsAsync(
            fixture.Candidate.Id, new(2, 10, JobApplicationStatus.Shortlisted));

        Assert.Equal(fixture.Candidate.Id, fixture.Repository.ListedForUserId);
        Assert.Equal(2, fixture.Repository.LastQuery!.PageNumber);
        Assert.Equal(10, fixture.Repository.LastQuery.PageSize);
    }

    private static Fixture CreateFixture()
    {
        var candidate = new User
        {
            Id = Guid.NewGuid(),
            Email = "candidate@example.test",
            FirstName = "Casey",
            LastName = "Patel",
            RoleId = SystemRoleIds.Candidate,
            EmailConfirmed = true,
            Status = UserStatus.Active
        };
        var company = new Company { Id = Guid.NewGuid(), Name = "Example Co", Slug = "example" };
        var job = new Job
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            Title = "Engineer",
            Slug = "engineer",
            Status = JobStatus.Published
        };
        var repository = new FakeCandidateRepository
        {
            Candidate = candidate,
            AvailableJob = new(job.Id, job.Title, job.Slug, company.Name)
        };
        var dashboard = new FakeDashboardRepository();
        var storage = new FakeResumeStorage();
        var unitOfWork = new FakeUnitOfWork();
        var audit = new AuditWriterTestDouble();
        var service = new CandidateService(
            repository, dashboard, storage, unitOfWork, audit,
            new UpdateCandidateProfileRequestValidator(), new CandidatePageQueryValidator(),
            new JobApplicationQueryValidator(), new CreateJobApplicationRequestValidator(),
            TimeProvider.System);
        return new(service, repository, dashboard, storage, unitOfWork, audit, candidate, job);
    }

    private sealed record Fixture(
        CandidateService Service,
        FakeCandidateRepository Repository,
        FakeDashboardRepository Dashboard,
        FakeResumeStorage Storage,
        FakeUnitOfWork UnitOfWork,
        AuditWriterTestDouble Audit,
        User Candidate,
        Job Job)
    {
        public JobApplication CreateApplication(JobApplicationStatus status) => new()
        {
            UserId = Candidate.Id,
            JobId = Job.Id,
            Job = Job,
            Status = status,
            SubmittedAtUtc = DateTime.UtcNow
        };
    }

    private sealed class FakeCandidateRepository : ICandidateRepository
    {
        public User? Candidate { get; set; }
        public CandidateJob? AvailableJob { get; set; }
        public bool HasMembership { get; set; } = true;
        public bool HasPriorApplication { get; set; }
        public JobApplication? OwnedApplication { get; set; }
        public List<JobApplication> AddedApplications { get; } = [];
        public HashSet<string> ReferencedResumeKeys { get; } = [];
        public Guid? ListedForUserId { get; private set; }
        public JobApplicationQuery? LastQuery { get; private set; }

        public Task<User?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Candidate?.Id == userId ? Candidate : null);
        public Task<CandidateJob?> GetAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AvailableJob?.Id == jobId ? AvailableJob : null);
        public Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasMembership);
        public Task<bool> IsResumeReferencedAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReferencedResumeKeys.Contains(storageKey));
        public Task<JobApplication?> GetApplicationAsync(
            Guid userId, Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OwnedApplication?.UserId == userId && OwnedApplication.Id == applicationId
                ? OwnedApplication
                : null);
        public Task<bool> HasApplicationAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasPriorApplication);
        public Task AddApplicationAsync(
            JobApplication application, CancellationToken cancellationToken = default)
        {
            AddedApplications.Add(application);
            return Task.CompletedTask;
        }
        public Task<(IReadOnlyCollection<JobApplicationResponse> Items, int TotalCount)> GetApplicationsAsync(
            Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default)
        {
            ListedForUserId = userId;
            LastQuery = query;
            return Task.FromResult(((IReadOnlyCollection<JobApplicationResponse>)[], 0));
        }
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        public bool JobAvailable { get; set; } = true;
        public bool AlreadySaved { get; set; }
        public List<SavedJob> Added { get; } = [];
        public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);
        public Task<(IReadOnlyCollection<SavedJobResponse> Items, int TotalCount)> GetSavedJobsAsync(
            Guid userId, DashboardQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<SavedJobResponse>)[], 0));
        public Task<bool> IsAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(JobAvailable);
        public Task<bool> IsJobSavedAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AlreadySaved);
        public Task AddSavedJobAsync(SavedJob savedJob, CancellationToken cancellationToken = default)
        {
            Added.Add(savedJob);
            return Task.CompletedTask;
        }
        public Task<SavedJob?> GetSavedJobAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SavedJob?>(null);
        public void RemoveSavedJob(SavedJob savedJob) { }
        public Task<(IReadOnlyCollection<AppliedJobHistoryResponse> Items, int TotalCount)> GetAppliedJobsAsync(
            Guid userId, DashboardQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<AppliedJobHistoryResponse>)[], 0));
        public Task<(IReadOnlyCollection<NotificationResponse> Items, int TotalCount, int UnreadCount)> GetNotificationsAsync(
            Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<NotificationResponse>)[], 0, 0));
        public Task<Notification?> GetNotificationAsync(
            Guid userId, Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Notification?>(null);
        public Task<int> MarkAllNotificationsReadAsync(
            Guid userId, DateTime readAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class FakeResumeStorage : IResumeStorage
    {
        public List<string> Stored { get; } = [];
        public List<string> Deleted { get; } = [];
        public Task<string> StoreAsync(
            Stream content, string extension, CancellationToken cancellationToken = default)
        {
            var key = $"{Guid.NewGuid():N}{extension}";
            Stored.Add(key);
            return Task.FromResult(key);
        }
        public Task<Stream?> OpenReadAsync(
            string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
