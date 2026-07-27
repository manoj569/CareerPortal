using JobPortal.Application.Abstractions.AdminDashboard;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;

namespace JobPortal.Application.Features.AdminDashboard;

public sealed class AdminDashboardService(
    IAdminDashboardRepository repository,
    TimeProvider timeProvider) : IAdminDashboardService
{
    public Task<AdminDashboardStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) =>
        repository.GetStatisticsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<IReadOnlyCollection<RecentPaymentResponse>> GetRecentPaymentsAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidateLimit(limit, 100);
        return repository.GetRecentPaymentsAsync(limit, cancellationToken);
    }

    public Task<IReadOnlyCollection<RecentUserResponse>> GetRecentUsersAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidateLimit(limit, 100);
        return repository.GetRecentUsersAsync(limit, cancellationToken);
    }

    public async Task<AdminDashboardCharts> GetChartsAsync(
        ChartQuery query, int distributionLimit, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidateLimit(distributionLimit, 50);
        var toUtc = query.ToUtc ?? timeProvider.GetUtcNow().UtcDateTime;
        var fromUtc = query.FromUtc ?? toUtc.AddDays(-30);
        if (fromUtc >= toUtc) throw new BadRequestException("FromUtc must be earlier than ToUtc.");
        if (toUtc - fromUtc > TimeSpan.FromDays(730))
            throw new BadRequestException("Chart range cannot exceed 730 days.");
        if (!Enum.IsDefined(query.Interval))
            throw new BadRequestException("Invalid chart interval.");

        // Deliberately sequential: a scoped EF DbContext does not support concurrent operations.
        var revenue = await repository.GetRevenueChartAsync(fromUtc, toUtc, query.Interval, cancellationToken);
        var users = await repository.GetUserChartAsync(fromUtc, toUtc, query.Interval, cancellationToken);
        var jobs = await repository.GetJobChartAsync(fromUtc, toUtc, query.Interval, cancellationToken);
        var categories = await repository.GetCategoryDistributionAsync(distributionLimit, toUtc, cancellationToken);
        var companies = await repository.GetCompanyDistributionAsync(distributionLimit, toUtc, cancellationToken);
        return new AdminDashboardCharts(revenue, users, jobs, categories, companies);
    }

}
