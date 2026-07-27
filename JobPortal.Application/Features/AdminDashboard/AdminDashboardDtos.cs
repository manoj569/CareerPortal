using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.AdminDashboard;

public sealed record RevenueTotal(string CurrencyCode, decimal Total, decimal ThisMonth);
public sealed record AdminDashboardStatistics(
    IReadOnlyCollection<RevenueTotal> Revenue, int TotalUsers, int PaidUsers,
    int TotalJobs, int PublishedJobs, int FeaturedJobs, int ExpiredJobs,
    int TotalCategories, int TotalCompanies, DateTime GeneratedAtUtc);

public sealed record RecentPaymentResponse(
    Guid Id, Guid UserId, string UserName, string UserEmail, decimal Amount,
    string CurrencyCode, PaymentStatus Status, PaymentProvider Provider,
    string? ProviderPaymentId, DateTime? PaidAtUtc, DateTime CreatedAtUtc);

public sealed record RecentUserResponse(
    Guid Id, string Email, string FirstName, string LastName, UserStatus Status,
    bool EmailConfirmed, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);

public enum ChartInterval { Day = 1, Month }

public sealed record ChartQuery(
    DateTime? FromUtc = null, DateTime? ToUtc = null,
    ChartInterval Interval = ChartInterval.Day);

public sealed record RevenueChartPoint(
    DateTime PeriodStartUtc, string CurrencyCode, decimal Revenue, int Payments, int PaidUsers);
public sealed record CountChartPoint(DateTime PeriodStartUtc, int Count);
public sealed record DistributionChartPoint(Guid Id, string Label, int Value);
public sealed record AdminDashboardCharts(
    IReadOnlyCollection<RevenueChartPoint> Revenue,
    IReadOnlyCollection<CountChartPoint> Users,
    IReadOnlyCollection<CountChartPoint> Jobs,
    IReadOnlyCollection<DistributionChartPoint> Categories,
    IReadOnlyCollection<DistributionChartPoint> Companies);
