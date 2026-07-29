using JobPortal.Application.Abstractions.Jobs;
using Microsoft.AspNetCore.OutputCaching;

namespace JobPortal.API.HostedServices;

public sealed class JobExpiryHostedService(
    IServiceScopeFactory scopeFactory,
    IOutputCacheStore outputCache,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<JobExpiryHostedService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> WorkerDisabled =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2001, nameof(WorkerDisabled)),
            "Automatic job expiry is disabled.");

    private static readonly Action<ILogger, int, Exception?> JobsExpired =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2002, nameof(JobsExpired)),
            "Automatic job expiry marked {ExpiredJobCount} jobs as Expired.");

    private static readonly Action<ILogger, Exception?> ExpiryFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2003, nameof(ExpiryFailed)),
            "Automatic job expiry cycle failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("JobExpiry:Enabled", true))
        {
            WorkerDisabled(logger, null);
            return;
        }

        var intervalMinutes = Math.Clamp(
            configuration.GetValue("JobExpiry:IntervalMinutes", 15), 1, 1440);
        if (configuration.GetValue("JobExpiry:RunOnStartup", true))
            await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(intervalMinutes), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunCycleAsync(stoppingToken);
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var expiryService = scope.ServiceProvider.GetRequiredService<IJobExpiryService>();
            var expiredCount = await expiryService.ExpireOverdueAsync(cancellationToken);
            if (expiredCount == 0)
                return;

            await outputCache.EvictByTagAsync("public-jobs", cancellationToken);
            JobsExpired(logger, expiredCount, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ExpiryFailed(logger, exception);
        }
    }
}
