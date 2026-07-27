using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Dashboard;

public interface IDashboardService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentResponse>> GetPaymentHistoryAsync(Guid userId, DashboardQuery query, CancellationToken cancellationToken = default);
    Task<PagedResponse<SavedJobResponse>> GetSavedJobsAsync(Guid userId, DashboardQuery query, CancellationToken cancellationToken = default);
    Task SaveJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task RemoveSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task<PagedResponse<AppliedJobHistoryResponse>> GetAppliedJobsAsync(Guid userId, DashboardQuery query, CancellationToken cancellationToken = default);
    Task<(PagedResponse<NotificationResponse> Page, int UnreadCount)> GetNotificationsAsync(Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default);
    Task MarkNotificationReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task MarkAllNotificationsReadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
