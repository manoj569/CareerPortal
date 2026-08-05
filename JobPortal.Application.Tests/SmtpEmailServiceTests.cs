using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class SmtpEmailServiceTests
{
    [Fact]
    public void PasswordResetUrlAllowsOnlyAbsoluteHttpAndEncodesParameters()
    {
        const string email = "candidate+reset@example.com";
        const string token = "token/with+reserved=characters";

        var result = SmtpEmailService.BuildPasswordResetUrl(
            "https://careers.example.test/reset?source=portal#reset",
            email,
            token);

        Assert.NotNull(result);
        Assert.Equal("https", result.Scheme);
        Assert.Equal("careers.example.test", result.Host);
        Assert.Contains("source=portal", result.Query, StringComparison.Ordinal);
        Assert.Contains(
            "email=candidate%2Breset%40example.com",
            result.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "token=token%2Fwith%2Breserved%3Dcharacters",
            result.Query,
            StringComparison.Ordinal);
        Assert.Equal("#reset", result.Fragment);
        Assert.Null(SmtpEmailService.BuildPasswordResetUrl(
            "javascript:alert(1)",
            email,
            token));
        Assert.Null(SmtpEmailService.BuildPasswordResetUrl(
            "/relative/reset",
            email,
            token));
    }

    [Fact]
    public async Task DisabledDeliveryDoesNotLogResetSecrets()
    {
        const string token = "raw-password-reset-token";
        const string email = "candidate@example.com";
        const string resetUrl = "http://localhost:5173/reset-password";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Enabled"] = bool.FalseString,
                ["Email:PasswordResetUrl"] = resetUrl
            })
            .Build();
        var logger = new CollectingLogger<SmtpEmailService>();
        var service = new SmtpEmailService(configuration, logger);

        var result = await service.SendPasswordResetAsync(
            new User
            {
                Email = email,
                FirstName = "Casey"
            },
            token);

        Assert.Equal(EmailDeliveryResult.Disabled, result);
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains(token, StringComparison.Ordinal) ||
            message.Contains(email, StringComparison.OrdinalIgnoreCase) ||
            message.Contains(resetUrl, StringComparison.Ordinal));
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
