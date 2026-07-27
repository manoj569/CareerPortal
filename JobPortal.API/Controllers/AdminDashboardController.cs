using JobPortal.Application.Abstractions.AdminDashboard;
using JobPortal.Application.Features.AdminDashboard;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/dashboard")]
[Produces("application/json")]
public sealed class AdminDashboardController(IAdminDashboardService dashboard) : ControllerBase
{
    [HttpGet("statistics")]
    public async Task<ActionResult<ApiResponse<AdminDashboardStatistics>>> Statistics(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminDashboardStatistics>(
            await dashboard.GetStatisticsAsync(cancellationToken)));

    [HttpGet("recent-payments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<RecentPaymentResponse>>>> RecentPayments(
        [FromQuery] int limit = 10, CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<RecentPaymentResponse>>(
            await dashboard.GetRecentPaymentsAsync(limit, cancellationToken)));

    [HttpGet("recent-users")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<RecentUserResponse>>>> RecentUsers(
        [FromQuery] int limit = 10, CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<RecentUserResponse>>(
            await dashboard.GetRecentUsersAsync(limit, cancellationToken)));

    [HttpGet("charts")]
    public async Task<ActionResult<ApiResponse<AdminDashboardCharts>>> Charts(
        [FromQuery] ChartQuery query, [FromQuery] int distributionLimit = 10,
        CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<AdminDashboardCharts>(
            await dashboard.GetChartsAsync(query, distributionLimit, cancellationToken)));
}
