using JobPortal.Application.Features.AdminDashboard;

namespace JobPortal.Application.Abstractions.AdminDashboard;

public interface IAdminDashboardService
{
    Task<AdminDashboardStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RecentPaymentResponse>> GetRecentPaymentsAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RecentUserResponse>> GetRecentUsersAsync(int limit, CancellationToken cancellationToken = default);
    Task<AdminDashboardCharts> GetChartsAsync(ChartQuery query, int distributionLimit, CancellationToken cancellationToken = default);
}
