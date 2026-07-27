using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Dashboard;

public sealed record UserProfileResponse(
    Guid Id, string Email, string FirstName, string LastName, string? PhoneNumber,
    string? ProfileImageUrl, string? Headline, string? Bio, bool EmailConfirmed,
    DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);
public sealed record UpdateUserProfileRequest(
    string FirstName, string LastName, string? PhoneNumber,
    string? ProfileImageUrl, string? Headline, string? Bio);
public sealed record DashboardQuery(int PageNumber = 1, int PageSize = 20);
public sealed record SavedJobResponse(Guid SavedJobId, DateTime SavedAtUtc, PublicJobSummary Job);
public sealed record AppliedJobHistoryResponse(
    Guid Id, Guid JobId, string JobTitle, string JobSlug, string CompanyName,
    JobHistoryAction Action, DateTime OccurredAtUtc, string? Notes);
public sealed record NotificationResponse(
    Guid Id, string Title, string Message, NotificationType Type, string? ActionUrl,
    bool IsRead, DateTime? ReadAtUtc, DateTime CreatedAtUtc);
public sealed record NotificationPage(PagedResponse<NotificationResponse> Page, int UnreadCount);
