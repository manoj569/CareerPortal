using System.Net;
using System.Net.Mail;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

public sealed class SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger) : IEmailService
{
    private static readonly Action<ILogger, string, Exception?> SmtpNotConfigured =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1001, nameof(SmtpNotConfigured)),
            "Password reset email for {Email} was not sent because SMTP is not configured.");

    public async Task SendPasswordResetAsync(User user, string resetToken, CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:Smtp:Host"];
        var sender = configuration["Email:FromAddress"];
        var resetUrl = $"{configuration["Email:PasswordResetUrl"]?.TrimEnd('/')}?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(resetToken)}";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender))
        {
            SmtpNotConfigured(logger, user.Email, null);
            return;
        }

        using var client = new SmtpClient(host, configuration.GetValue<int?>("Email:Smtp:Port") ?? 587)
        {
            EnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true),
            Credentials = new NetworkCredential(configuration["Email:Smtp:Username"], configuration["Email:Smtp:Password"])
        };
        using var message = new MailMessage(sender, user.Email)
        {
            Subject = "Reset your Job Portal password",
            Body = $"Use the following secure link to reset your password. It expires in 30 minutes: {resetUrl}",
            IsBodyHtml = false
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
