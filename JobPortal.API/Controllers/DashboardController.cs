using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Dashboard;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
[Produces("application/json")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<UserProfileResponse>>> Profile(CancellationToken cancellationToken) =>
        Ok(new ApiResponse<UserProfileResponse>(
            await dashboard.GetProfileAsync(User.GetRequiredUserId(), cancellationToken)));

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<UserProfileResponse>>> UpdateProfile(
        [FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<UserProfileResponse>(
            await dashboard.UpdateProfileAsync(User.GetRequiredUserId(), request, cancellationToken),
            "Profile updated successfully."));

    [HttpGet("memberships")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<MembershipResponse>>>> Memberships(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<MembershipResponse>>(
            await dashboard.GetMembershipsAsync(User.GetRequiredUserId(), cancellationToken)));

    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<PagedResponse<PaymentResponse>>>> Payments(
        [FromQuery] DashboardQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<PaymentResponse>>(
            await dashboard.GetPaymentHistoryAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("saved-jobs")]
    public async Task<ActionResult<ApiResponse<PagedResponse<SavedJobResponse>>>> SavedJobs(
        [FromQuery] DashboardQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<SavedJobResponse>>(
            await dashboard.GetSavedJobsAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpPut("saved-jobs/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SaveJob(Guid jobId, CancellationToken cancellationToken)
    {
        await dashboard.SaveJobAsync(User.GetRequiredUserId(), jobId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("saved-jobs/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveSavedJob(Guid jobId, CancellationToken cancellationToken)
    {
        await dashboard.RemoveSavedJobAsync(User.GetRequiredUserId(), jobId, cancellationToken);
        return NoContent();
    }

    [HttpGet("applied-jobs")]
    public async Task<ActionResult<ApiResponse<PagedResponse<AppliedJobHistoryResponse>>>> AppliedJobs(
        [FromQuery] DashboardQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<AppliedJobHistoryResponse>>(
            await dashboard.GetAppliedJobsAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("notifications")]
    public async Task<ActionResult<ApiResponse<NotificationPage>>> Notifications(
        [FromQuery] DashboardQuery query, [FromQuery] bool? isRead,
        CancellationToken cancellationToken)
    {
        var result = await dashboard.GetNotificationsAsync(
            User.GetRequiredUserId(), query, isRead, cancellationToken);
        return Ok(new ApiResponse<NotificationPage>(new NotificationPage(result.Page, result.UnreadCount)));
    }

    [HttpPut("notifications/{notificationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkNotificationRead(Guid notificationId, CancellationToken cancellationToken)
    {
        await dashboard.MarkNotificationReadAsync(User.GetRequiredUserId(), notificationId, cancellationToken);
        return NoContent();
    }

    [HttpPut("notifications/read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken cancellationToken)
    {
        await dashboard.MarkAllNotificationsReadAsync(User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await dashboard.ChangePasswordAsync(User.GetRequiredUserId(), request, cancellationToken);
        return NoContent();
    }
}
