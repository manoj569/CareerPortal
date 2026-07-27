using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Dashboard;
using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Common.Validation;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Dashboard;

public sealed class DashboardService(
    IDashboardRepository dashboard,
    IMembershipService membershipService,
    IPaymentService paymentService,
    IAuthService authService,
    IUnitOfWork unitOfWork,
    IValidator<UpdateUserProfileRequest> profileValidator,
    TimeProvider timeProvider) : IDashboardService
{
    public async Task<UserProfileResponse> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        MapProfile(await RequiredUserAsync(userId, cancellationToken));

    public async Task<UserProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        await profileValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredUserAsync(userId, cancellationToken);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = TextNormalizer.TrimOrNull(request.PhoneNumber);
        user.ProfileImageUrl = TextNormalizer.TrimOrNull(request.ProfileImageUrl);
        user.Headline = TextNormalizer.TrimOrNull(request.Headline);
        user.Bio = TextNormalizer.TrimOrNull(request.Bio);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapProfile(user);
    }

    public Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        membershipService.GetMyMembershipsAsync(userId, cancellationToken);

    public Task<PagedResponse<PaymentResponse>> GetPaymentHistoryAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        return paymentService.GetPaymentsAsync(
            userId, new HistoryQuery(query.PageNumber, query.PageSize), cancellationToken);
    }

    public async Task<PagedResponse<SavedJobResponse>> GetSavedJobsAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await dashboard.GetSavedJobsAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task SaveJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!await dashboard.IsAvailableJobAsync(jobId, cancellationToken))
            throw new NotFoundException("Job was not found.");
        if (await dashboard.IsJobSavedAsync(userId, jobId, cancellationToken)) return;
        await dashboard.AddSavedJobAsync(new SavedJob { UserId = userId, JobId = jobId }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var savedJob = await dashboard.GetSavedJobAsync(userId, jobId, cancellationToken);
        if (savedJob is null) return;
        dashboard.RemoveSavedJob(savedJob);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<AppliedJobHistoryResponse>> GetAppliedJobsAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await dashboard.GetAppliedJobsAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<(PagedResponse<NotificationResponse> Page, int UnreadCount)> GetNotificationsAsync(
        Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await dashboard.GetNotificationsAsync(userId, query, isRead, cancellationToken);
        return (new(result.Items, query.PageNumber, query.PageSize, result.TotalCount), result.UnreadCount);
    }

    public async Task MarkNotificationReadAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await dashboard.GetNotificationAsync(userId, notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification was not found.");
        if (notification.IsRead) return;
        notification.IsRead = true;
        notification.ReadAtUtc = UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllNotificationsReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _ = await dashboard.MarkAllNotificationsReadAsync(userId, UtcNow, cancellationToken);

    public Task ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        authService.ChangePasswordAsync(userId, request, cancellationToken);

    private async Task<User> RequiredUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dashboard.GetUserAsync(userId, cancellationToken) ?? throw new UnauthorizedException();
    private static UserProfileResponse MapProfile(User x) => new(
        x.Id, x.Email, x.FirstName, x.LastName, x.PhoneNumber, x.ProfileImageUrl,
        x.Headline, x.Bio, x.EmailConfirmed, x.CreatedAtUtc, x.LastLoginAtUtc);
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
