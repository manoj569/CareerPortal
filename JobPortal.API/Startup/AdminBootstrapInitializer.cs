using JobPortal.Application.Features.Authentication;

namespace JobPortal.API.Startup;

public sealed class AdminBootstrapInitializer(
    IConfiguration configuration,
    AdminBootstrapService bootstrapService,
    ILogger<AdminBootstrapInitializer> logger)
{
    private static readonly Action<ILogger, Exception?> Disabled =
        LoggerMessage.Define(LogLevel.Information, new EventId(1001, nameof(Disabled)),
            "Administrator bootstrap skipped because it is disabled.");
    private static readonly Action<ILogger, Exception?> AlreadyExists =
        LoggerMessage.Define(LogLevel.Information, new EventId(1002, nameof(AlreadyExists)),
            "Administrator bootstrap skipped because the Administrator account already exists.");
    private static readonly Action<ILogger, Exception?> Completed =
        LoggerMessage.Define(LogLevel.Information, new EventId(1003, nameof(Completed)),
            "Administrator bootstrap completed.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection("BootstrapAdmin");
        var result = await bootstrapService.InitializeAsync(new AdminBootstrapSettings(
            section.GetValue<bool>("Enabled"), section["Email"], section["Password"],
            section["FirstName"], section["LastName"]), cancellationToken);
        if (result == AdminBootstrapResult.Disabled)
        {
            Disabled(logger, null);
            return;
        }
        if (result == AdminBootstrapResult.AlreadyExists)
        {
            AlreadyExists(logger, null);
            return;
        }
        Completed(logger, null);
    }
}
