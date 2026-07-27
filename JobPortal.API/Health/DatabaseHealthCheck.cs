using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobPortal.API.Health;

public sealed class DatabaseHealthCheck(
    JobPortalDbContext dbContext,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    private static readonly Action<ILogger, Exception?> DatabaseCheckFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2001, nameof(DatabaseCheckFailed)),
            "Database readiness check failed");

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database connection is available.")
                : HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
        catch (Exception exception)
        {
            DatabaseCheckFailed(logger, exception);
            return HealthCheckResult.Unhealthy("Database readiness check failed.");
        }
    }
}
