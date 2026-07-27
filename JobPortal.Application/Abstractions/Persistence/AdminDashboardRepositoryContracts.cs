using JobPortal.Application.Features.AdminDashboard;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IAdminDashboardRepository
{
    Task<AdminDashboardStatistics> GetStatisticsAsync(DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RecentPaymentResponse>> GetRecentPaymentsAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RecentUserResponse>> GetRecentUsersAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RevenueChartPoint>> GetRevenueChartAsync(DateTime fromUtc, DateTime toUtc, ChartInterval interval, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CountChartPoint>> GetUserChartAsync(DateTime fromUtc, DateTime toUtc, ChartInterval interval, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CountChartPoint>> GetJobChartAsync(DateTime fromUtc, DateTime toUtc, ChartInterval interval, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DistributionChartPoint>> GetCategoryDistributionAsync(int limit, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DistributionChartPoint>> GetCompanyDistributionAsync(int limit, DateTime utcNow, CancellationToken cancellationToken = default);
}
