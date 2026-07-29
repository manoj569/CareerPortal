using System.Net;
using System.Net.Mail;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

public sealed class SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger) : IEmailService
{
    private static readonly Action<ILogger, Exception?> DeliveryDisabled =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1001, nameof(DeliveryDisabled)),
            "Transactional email delivery is disabled.");
    private static readonly Action<ILogger, string, Exception?> DeliveryFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1002, nameof(DeliveryFailed)),
            "Transactional email delivery failed for message type {MessageType}.");

    public Task<EmailDeliveryResult> SendEmailVerificationAsync(
        User user, string verificationToken, CancellationToken cancellationToken = default) =>
        SendAsync(user.Email, "Verify your Job Portal email",
            $"Verify your email within 24 hours using this secure link: {BuildUrl("Email:VerificationUrl", user.Email, verificationToken)}",
            "email-verification", cancellationToken);

    public Task<EmailDeliveryResult> SendPasswordResetAsync(
        User user, string resetToken, CancellationToken cancellationToken = default) =>
        SendAsync(user.Email, "Reset your Job Portal password",
            $"Reset your password within 30 minutes using this secure link: {BuildUrl("Email:PasswordResetUrl", user.Email, resetToken)}",
            "password-reset", cancellationToken);

    public Task<EmailDeliveryResult> SendApplicationStatusAsync(
        User user, string jobTitle, JobApplicationStatus status,
        CancellationToken cancellationToken = default)
    {
        var safeJobTitle = jobTitle.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var statusText = status switch
        {
            JobApplicationStatus.Shortlisted => "shortlisted",
            JobApplicationStatus.Rejected => "not selected",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status), status, "Only terminal review statuses are emailed.")
        };
        return SendAsync(
            user.Email,
            $"Application update - {safeJobTitle}",
            $"Hello {user.FirstName}, your application for {safeJobTitle} has been {statusText}.",
            $"application-{status.ToString().ToLowerInvariant()}",
            cancellationToken);
    }

    private async Task<EmailDeliveryResult> SendAsync(
        string recipient, string subject, string body, string messageType, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Email:Enabled", false))
        {
            DeliveryDisabled(logger, null);
            return EmailDeliveryResult.Disabled;
        }

        try
        {
            using var client = new SmtpClient(
                configuration["Email:Smtp:Host"],
                configuration.GetValue<int>("Email:Smtp:Port"))
            {
                EnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", true),
                Credentials = new NetworkCredential(
                    configuration["Email:Smtp:Username"],
                    configuration["Email:Smtp:Password"])
            };
            using var message = new MailMessage(configuration["Email:FromAddress"]!, recipient)
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            await client.SendMailAsync(message, cancellationToken);
            return EmailDeliveryResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DeliveryFailed(logger, messageType, exception);
            return EmailDeliveryResult.Failed;
        }
    }

    private string BuildUrl(string configurationKey, string email, string token)
    {
        var baseUrl = configuration[configurationKey]
            ?? throw new InvalidOperationException($"{configurationKey} is not configured.");
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{separator}email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
