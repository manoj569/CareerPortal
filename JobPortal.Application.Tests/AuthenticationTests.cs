using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Legal;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Authentication;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AuthenticationTests
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Manoj Shekapure", true)]
    [InlineData("सुमित कुमार", true)]
    [InlineData("Cher", true)]
    [InlineData("Manoj  Shekapure", false)]
    [InlineData("Manoj7 Shekapure", false)]
    [InlineData("Manoj-Shekapure", false)]
    [InlineData("Manoj 🙂", false)]
    [InlineData("", false)]
    public async Task FullNameAllowsOnlyUnicodeLettersAndSingleSpaces(
        string fullName,
        bool expectedValid)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRegistration() with { FullName = fullName });

        Assert.Equal(
            expectedValid,
            !result.Errors.Any(error => error.PropertyName == "FullName"));
    }

    [Fact]
    public async Task RegistrationPersistsOnlyPendingHashedChallenge()
    {
        var fixture = CreateFixture();
        fixture.Sms.BeforeSend = () =>
            Assert.True(fixture.UnitOfWork.SaveCount > 0);

        var response = await fixture.Service.RegisterAsync(
            ValidRegistration() with
            {
                FullName = "  Manoj Shekapure  ",
                Email = "  User@Example.COM  "
            });

        Assert.NotEqual(Guid.Empty, response.ChallengeId);
        Assert.Empty(fixture.Users.Items);
        var pending = Assert.Single(fixture.Challenges.Pending);
        var challenge = Assert.Single(fixture.Challenges.OtpChallenges);
        Assert.Equal("Manoj", pending.FirstName);
        Assert.Equal("Shekapure", pending.LastName);
        Assert.Equal("user@example.com", pending.NormalizedEmail);
        Assert.Equal("+919876543210", pending.NormalizedPhoneNumber);
        Assert.NotEqual("abc123", pending.PasswordHash);
        Assert.DoesNotContain("abc123", pending.PasswordHash!, StringComparison.Ordinal);
        Assert.Equal(LegalDocumentCatalog.CurrentVersion, pending.TermsAndPrivacyVersion);
        Assert.Equal(Now, pending.TermsAndPrivacyAcceptedAtUtc);
        Assert.NotEqual(fixture.Sms.LastOtp, challenge.OtpHash);
        Assert.DoesNotContain(fixture.Sms.LastOtp!, challenge.OtpHash, StringComparison.Ordinal);
        Assert.Equal(OtpPurpose.Registration, challenge.Purpose);
        Assert.Equal(1, fixture.Sms.SendCount);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("sms_delivery_completed", StringComparison.Ordinal) &&
            message.Contains("status Sent", StringComparison.Ordinal));
        Assert.False(
            fixture.Logger.Messages.Any(message =>
                message.Contains(fixture.Sms.LastOtp!, StringComparison.Ordinal) ||
                message.Contains(pending.NormalizedPhoneNumber, StringComparison.Ordinal) ||
                message.Contains(pending.Email, StringComparison.OrdinalIgnoreCase) ||
                message.Contains("abc123", StringComparison.Ordinal)),
            "Authentication logs contained sensitive registration data.");
        Assert.DoesNotContain(
            "AccessToken",
            JsonSerializer.Serialize(response),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledRegistrationLogsCancellationWithoutAttemptingSms()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.RegisterAsync(
                ValidRegistration(),
                cancellation.Token));

        Assert.Equal(0, fixture.Sms.SendCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("request_cancelled_before_persist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClientCancellationAfterPersistenceDoesNotStrandOtpDelivery()
    {
        var fixture = CreateFixture();
        using var requestCancellation = new CancellationTokenSource();
        fixture.Sms.BeforeSend = requestCancellation.Cancel;

        var response = await fixture.Service.RegisterAsync(
            ValidRegistration(),
            requestCancellation.Token);

        Assert.NotEqual(Guid.Empty, response.ChallengeId);
        Assert.True(requestCancellation.IsCancellationRequested);
        Assert.False(fixture.Sms.LastCancellationToken.IsCancellationRequested);
        Assert.Equal(1, fixture.Sms.SendCount);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.DoesNotContain(fixture.Logger.Messages, message =>
            message.Contains(
                "request_cancelled_before_persist",
                StringComparison.Ordinal));
        Assert.Single(fixture.Challenges.Pending);
        Assert.Single(fixture.Challenges.OtpChallenges);
    }

    [Fact]
    public async Task CorrectRegistrationOtpCreatesOneActiveCandidateAndReplayIsIdempotent()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RegisterAsync(ValidRegistration());
        var otp = fixture.Sms.LastOtp!;

        var first = await fixture.Service.VerifyRegistrationOtpAsync(
            new(started.ChallengeId, otp));
        var replay = await fixture.Service.VerifyRegistrationOtpAsync(
            new(started.ChallengeId, otp));

        Assert.Equal("Registration successful. Please log in.", first.Message);
        Assert.Equal(first, replay);
        var user = Assert.Single(fixture.Users.Items);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.PhoneConfirmed);
        Assert.Equal(SystemRoleIds.Candidate, user.RoleId);
        Assert.Equal("Manoj", user.FirstName);
        Assert.Equal("Shekapure", user.LastName);
        Assert.Equal("+919876543210", user.NormalizedPhoneNumber);
        Assert.Null(Assert.Single(fixture.Challenges.Pending).PasswordHash);
        Assert.NotNull(Assert.Single(fixture.Challenges.OtpChallenges).ConsumedAtUtc);
        var audit = Assert.Single(fixture.Audit.Events);
        var auditJson = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain("123456", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("9876543210", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.com", auditJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongExpiredAndAttemptLimitedRegistrationOtpsAreRejected()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RegisterAsync(ValidRegistration());

        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<BadRequestException>(() =>
                fixture.Service.VerifyRegistrationOtpAsync(
                    new(started.ChallengeId, "000000")));
        Assert.Equal(
            5,
            Assert.Single(fixture.Challenges.OtpChallenges).FailedAttemptCount);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.VerifyRegistrationOtpAsync(
                new(started.ChallengeId, fixture.Sms.LastOtp!)));

        var expired = CreateFixture();
        var expiredStart = await expired.Service.RegisterAsync(ValidRegistration());
        expired.Time.Advance(TimeSpan.FromMinutes(6));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            expired.Service.VerifyRegistrationOtpAsync(
                new(expiredStart.ChallengeId, expired.Sms.LastOtp!)));
        Assert.Empty(expired.Users.Items);
    }

    [Fact]
    public async Task RegistrationResendEnforcesCooldownAndRotatesCode()
    {
        var fixture = CreateFixture("123456", "654321");
        var started = await fixture.Service.RegisterAsync(ValidRegistration());
        var firstHash = Assert.Single(fixture.Challenges.OtpChallenges).OtpHash;

        await fixture.Service.ResendRegistrationOtpAsync(
            new(started.ChallengeId));
        Assert.Equal(1, fixture.Sms.SendCount);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("send_skipped_cooldown", StringComparison.Ordinal));
        Assert.Equal(firstHash, Assert.Single(fixture.Challenges.OtpChallenges).OtpHash);
        fixture.Time.Advance(TimeSpan.FromSeconds(61));
        await fixture.Service.ResendRegistrationOtpAsync(new(started.ChallengeId));

        var challenge = Assert.Single(fixture.Challenges.OtpChallenges);
        Assert.NotEqual(firstHash, challenge.OtpHash);
        Assert.Equal("654321", fixture.Sms.LastOtp);
        Assert.Equal(2, fixture.Sms.SendCount);
        Assert.Equal(0, challenge.FailedAttemptCount);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.VerifyRegistrationOtpAsync(
                new(started.ChallengeId, "123456")));
    }

    [Fact]
    public async Task ExistingPendingRegistrationReturnsChallengeWithoutSendingAgain()
    {
        var fixture = CreateFixture();
        var first = await fixture.Service.RegisterAsync(ValidRegistration());

        var replay = await fixture.Service.RegisterAsync(ValidRegistration());

        Assert.Equal(first.ChallengeId, replay.ChallengeId);
        Assert.Equal(first.ExpiresAtUtc, replay.ExpiresAtUtc);
        Assert.DoesNotContain("sent", replay.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, fixture.Sms.SendCount);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("send_skipped_cooldown", StringComparison.Ordinal));
        Assert.Single(fixture.Challenges.Pending);
        Assert.Single(fixture.Challenges.OtpChallenges);
    }

    [Fact]
    public async Task ExistingPendingRegistrationAfterCooldownRotatesAndSendsAgain()
    {
        var fixture = CreateFixture("123456", "654321");
        var first = await fixture.Service.RegisterAsync(ValidRegistration());
        var challenge = Assert.Single(fixture.Challenges.OtpChallenges);
        var originalHash = challenge.OtpHash;
        fixture.Time.Advance(TimeSpan.FromSeconds(60));

        var replay = await fixture.Service.RegisterAsync(ValidRegistration());

        Assert.Equal(first.ChallengeId, replay.ChallengeId);
        Assert.Equal(2, fixture.Sms.SendCount);
        Assert.Equal("654321", fixture.Sms.LastOtp);
        Assert.NotEqual(originalHash, challenge.OtpHash);
        Assert.Equal(2, challenge.SendCount);
        Assert.Equal(0, challenge.FailedAttemptCount);
        Assert.Equal(Now.AddSeconds(60), challenge.LastSentAtUtc);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("otp_challenge_persisted", StringComparison.Ordinal) &&
            message.Contains("status rotated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RateLimitedRegistrationDoesNotSendAgain()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(ValidRegistration());
        var challenge = Assert.Single(fixture.Challenges.OtpChallenges);
        challenge.SendCount = 5;
        fixture.Time.Advance(TimeSpan.FromSeconds(60));

        await fixture.Service.RegisterAsync(ValidRegistration());

        Assert.Equal(1, fixture.Sms.SendCount);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("send_skipped_rate_limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegistrationResendLogsRateLimitWithoutSending()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RegisterAsync(ValidRegistration());
        var challenge = Assert.Single(fixture.Challenges.OtpChallenges);
        challenge.SendCount = 5;
        fixture.Time.Advance(TimeSpan.FromSeconds(61));

        await fixture.Service.ResendRegistrationOtpAsync(new(started.ChallengeId));

        Assert.Equal(1, fixture.Sms.SendCount);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("send_skipped_rate_limit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("abc12", false)]
    [InlineData("abcdef", true)]
    [InlineData("abc123", true)]
    [InlineData("ABCDEF", true)]
    [InlineData("!!!!!!", true)]
    public async Task RegistrationUsesSixCharacterMinimumWithoutComplexity(
        string password,
        bool expectedValid)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRegistration() with { Password = password });

        Assert.Equal(
            expectedValid,
            !result.Errors.Any(error => error.PropertyName == "Password"));
    }

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("+919876543210", false)]
    [InlineData("09876543210", false)]
    [InlineData("98765 43210", false)]
    [InlineData("98765-43210", false)]
    [InlineData("5876543210", false)]
    [InlineData("9999999999", false)]
    [InlineData("98765abcde", false)]
    public async Task RegistrationAcceptsOnlyTenDigitIndianMobile(
        string phoneNumber,
        bool expectedValid)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRegistration() with { PhoneNumber = phoneNumber });

        Assert.Equal(
            expectedValid,
            !result.Errors.Any(error => error.PropertyName == "PhoneNumber"));
    }

    [Fact]
    public async Task DuplicateIdentityResponseIsPrivateAndConcurrentConflictCreatesNoUser()
    {
        var duplicate = CreateFixture();
        duplicate.Users.Items.Add(NewUser());

        var response = await duplicate.Service.RegisterAsync(ValidRegistration());

        Assert.Equal(
            "Registration request accepted. Use resend OTP if needed.",
            response.Message);
        Assert.Equal(0, duplicate.Sms.SendCount);
        Assert.Contains(duplicate.Logger.Messages, message =>
            message.Contains("send_skipped_existing_user", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "user@example.com",
            JsonSerializer.Serialize(response),
            StringComparison.OrdinalIgnoreCase);

        var concurrent = CreateFixture();
        concurrent.UnitOfWork.ExceptionToThrow =
            new UniqueConstraintException("duplicate");
        var concurrentResponse = await concurrent.Service.RegisterAsync(
            ValidRegistration());
        Assert.Equal(response.Message, concurrentResponse.Message);
        Assert.Empty(concurrent.Users.Items);
        Assert.Equal(0, concurrent.Sms.SendCount);
    }

    [Fact]
    public async Task ConcurrentVerificationConflictReturnsSuccessWithoutDuplicateUser()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RegisterAsync(ValidRegistration());
        fixture.UnitOfWork.ExceptionToThrow =
            new UniqueConstraintException("duplicate");

        var response = await fixture.Service.VerifyRegistrationOtpAsync(
            new(started.ChallengeId, fixture.Sms.LastOtp!));
        var replay = await fixture.Service.VerifyRegistrationOtpAsync(
            new(started.ChallengeId, fixture.Sms.LastOtp!));

        Assert.Equal("Registration successful. Please log in.", response.Message);
        Assert.Equal(response, replay);
        Assert.Single(fixture.Users.Items);
    }

    [Fact]
    public async Task RegistrationRequiresConsentValidEmailAndRejectsOverPosting()
    {
        var validator = new RegisterRequestValidator();
        var noConsent = await validator.ValidateAsync(
            ValidRegistration() with { HasAcceptedTermsAndPrivacy = false });
        var badEmail = await validator.ValidateAsync(
            ValidRegistration() with { Email = "not-an-email" });
        Assert.Contains(
            noConsent.Errors,
            error => error.PropertyName == "HasAcceptedTermsAndPrivacy");
        Assert.Contains(badEmail.Errors, error => error.PropertyName == "Email");

        const string json =
            """
            {
              "fullName":"Manoj Shekapure",
              "email":"user@example.com",
              "password":"abc123",
              "phoneNumber":"9876543210",
              "hasAcceptedTermsAndPrivacy":true,
              "role":"Administrator"
            }
            """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RegisterRequest>(json, WebJson));
    }

    [Fact]
    public async Task PasswordLoginSupportsNormalizedEmailMobileAndAdministrator()
    {
        var fixture = CreateFixture();
        var candidate = NewUser();
        var administrator = NewUser(
            "admin@example.com",
            "+919123456780",
            SystemRoleIds.Administrator,
            "Administrator");
        fixture.Users.Items.AddRange([candidate, administrator]);

        var emailLogin = await fixture.Service.LoginAsync(
            new(" USER@EXAMPLE.COM ", "abc123"),
            null);
        var mobileLogin = await fixture.Service.LoginAsync(
            new("9876543210", "abc123"),
            null);
        var adminLogin = await fixture.Service.LoginAsync(
            new("admin@example.com", "abc123"),
            null);

        Assert.Equal(candidate.Id, emailLogin.User.Id);
        Assert.Equal(candidate.Id, mobileLogin.User.Id);
        Assert.Equal("Administrator", adminLogin.User.Role);
        var missing = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.LoginAsync(
                new("missing@example.com", "abc123"),
                null));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.LoginAsync(
                new("user@example.com", "wrong-password"),
                null));
        Assert.Equal(missing.Message, wrong.Message);
    }

    [Fact]
    public async Task ActiveCandidateCanLoginWithPurposeScopedMobileOtp()
    {
        var fixture = CreateFixture("112233");
        var user = NewUser();
        fixture.Users.Items.Add(user);

        var requested = await fixture.Service.RequestLoginOtpAsync(
            new("9876543210"));
        var response = await fixture.Service.LoginWithOtpAsync(
            new("9876543210", "112233"),
            "127.0.0.1");

        Assert.Equal(
            "If the mobile number is eligible, an OTP has been sent.",
            requested.Message);
        Assert.Equal(user.Id, response.User.Id);
        Assert.Single(fixture.RefreshTokens.Added);
        Assert.Equal(
            OtpPurpose.Login,
            Assert.Single(fixture.Challenges.OtpChallenges).Purpose);
        Assert.Equal(AuditAction.Login, Assert.Single(fixture.Audit.Events).Action);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.LoginWithOtpAsync(
                new("9876543210", "112233"),
                null));
    }

    [Fact]
    public async Task OtpRequestResponseDoesNotRevealWhetherMobileExists()
    {
        var missing = CreateFixture();
        var existing = CreateFixture();
        existing.Users.Items.Add(NewUser());

        var missingResponse = await missing.Service.RequestLoginOtpAsync(
            new("9876543210"));
        var existingResponse = await existing.Service.RequestLoginOtpAsync(
            new("9876543210"));

        Assert.Equal(missingResponse, existingResponse);
        Assert.Equal(0, missing.Sms.SendCount);
        Assert.Equal(1, existing.Sms.SendCount);
    }

    [Fact]
    public async Task LoginOtpCannotBeReusedForRegistration()
    {
        var fixture = CreateFixture("123456");
        var user = NewUser();
        fixture.Users.Items.Add(user);

        await fixture.Service.RequestLoginOtpAsync(new("9876543210"));
        var loginChallenge = Assert.Single(fixture.Challenges.OtpChallenges);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.VerifyRegistrationOtpAsync(
                new(loginChallenge.Id, "123456")));
        Assert.Empty(fixture.RefreshTokens.Added);
    }

    [Fact]
    public async Task PasswordResetRequestIsPrivacySafeAndPersistsHashBeforeEmail()
    {
        var invalidEmail = await new RequestPasswordResetRequestValidator()
            .ValidateAsync(new RequestPasswordResetRequest("not-an-email"));
        Assert.Contains(
            invalidEmail.Errors,
            error => error.PropertyName == "Email");

        var missing = CreateFixture();
        var fixture = CreateFixture();
        var user = NewUser();
        fixture.Users.Items.Add(user);
        fixture.Email.BeforeSend = () =>
            Assert.True(fixture.UnitOfWork.SaveCount > 0);

        var missingResponse = await missing.Service.RequestPasswordResetAsync(
            new("user@example.com"));
        var response = await fixture.Service.RequestPasswordResetAsync(
            new("  USER@Example.COM  "));

        Assert.Equal(missingResponse, response);
        Assert.Equal(
            "If an account exists for this email address, a password reset link has been sent.",
            response.Message);
        Assert.Equal(0, missing.Email.SendCount);
        Assert.Equal(1, fixture.Email.SendCount);
        Assert.Same(user, fixture.Email.User);
        Assert.NotNull(fixture.Email.LastRawToken);
        Assert.NotEqual(fixture.Email.LastRawToken, user.PasswordResetTokenHash);
        Assert.Equal(64, user.PasswordResetTokenHash!.Length);
        Assert.Equal(Now.AddMinutes(30), user.PasswordResetTokenExpiresAtUtc);
        Assert.Equal(0, fixture.Sms.SendCount);
        var auditJson = JsonSerializer.Serialize(Assert.Single(fixture.Audit.Events));
        Assert.DoesNotContain(fixture.Email.LastRawToken!, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, auditJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Logger.Messages, message =>
            message.Contains(
                fixture.Email.LastRawToken!,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task PasswordResetTokenIsSingleUseAndRevokesSessions()
    {
        var fixture = CreateFixture();
        var user = NewUser();
        fixture.Users.Items.Add(user);
        await fixture.Service.RequestPasswordResetAsync(new(user.Email));
        var rawToken = fixture.Email.LastRawToken!;
        var mismatch = await new CompletePasswordResetRequestValidator()
            .ValidateAsync(new CompletePasswordResetRequest(
                user.Email,
                rawToken,
                "newpass",
                "different"));
        Assert.Contains(
            mismatch.Errors,
            error => error.PropertyName == "ConfirmPassword");

        var completed = await fixture.Service.CompletePasswordResetAsync(
            new("  USER@EXAMPLE.COM ", rawToken, "newpass", "newpass"));

        Assert.Equal(
            "Password changed successfully. Please log in.",
            completed.Message);
        Assert.True(fixture.Passwords.Verify("newpass", user.PasswordHash));
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Null(user.PasswordResetTokenExpiresAtUtc);
        Assert.True(fixture.RefreshTokens.RevokedForUser);
        Assert.All(fixture.Audit.Events, audit => Assert.Equal(AuditAction.Update, audit.Action));
        var completionAudit = JsonSerializer.Serialize(fixture.Audit.Events.Last());
        Assert.DoesNotContain(rawToken, completionAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("newpass", completionAudit, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.CompletePasswordResetAsync(
                new(user.Email, rawToken, "again12", "again12")));
    }

    [Fact]
    public async Task InvalidExpiredAndInactivePasswordResetTokensAreRejected()
    {
        var invalid = CreateFixture();
        var invalidUser = NewUser();
        invalid.Users.Items.Add(invalidUser);
        await invalid.Service.RequestPasswordResetAsync(new(invalidUser.Email));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            invalid.Service.CompletePasswordResetAsync(
                new(invalidUser.Email, "wrong-token", "newpass", "newpass")));

        var expired = CreateFixture();
        var expiredUser = NewUser();
        expired.Users.Items.Add(expiredUser);
        await expired.Service.RequestPasswordResetAsync(new(expiredUser.Email));
        expired.Time.Advance(TimeSpan.FromMinutes(31));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            expired.Service.CompletePasswordResetAsync(
                new(
                    expiredUser.Email,
                    expired.Email.LastRawToken!,
                    "newpass",
                    "newpass")));

        var inactive = CreateFixture();
        var inactiveUser = NewUser();
        inactiveUser.Status = UserStatus.Inactive;
        inactive.Users.Items.Add(inactiveUser);
        var inactiveResponse = await inactive.Service.RequestPasswordResetAsync(
            new(inactiveUser.Email));
        Assert.Equal(
            "If an account exists for this email address, a password reset link has been sent.",
            inactiveResponse.Message);
        Assert.Equal(0, inactive.Email.SendCount);
        Assert.Null(inactiveUser.PasswordResetTokenHash);
    }

    [Fact]
    public void LegalDocumentsAndApiContractsArePublicAndApplicationOwned()
    {
        var terms = LegalDocumentCatalog.TermsOfUse();
        var privacy = LegalDocumentCatalog.PrivacyPolicy();
        Assert.Equal(LegalDocumentCatalog.CurrentVersion, terms.Version);
        Assert.Equal("text/plain", terms.ContentType);
        Assert.NotEmpty(terms.Content);
        Assert.Equal(terms.EffectiveDate, privacy.EffectiveDate);

        var root = FindRepositoryRoot();
        var legalController = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.API",
            "Controllers",
            "LegalController.cs"));
        var authController = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.API",
            "Controllers",
            "AuthController.cs"));
        Assert.Contains("terms-of-use", legalController, StringComparison.Ordinal);
        Assert.Contains("privacy-policy", legalController, StringComparison.Ordinal);
        Assert.Contains("verify-registration-otp", authController, StringComparison.Ordinal);
        Assert.Contains("login-with-otp", authController, StringComparison.Ordinal);
        Assert.Contains("request-password-reset", authController, StringComparison.Ordinal);
        Assert.Contains("complete-password-reset", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("request-password-reset-otp", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-password-reset-otp", authController, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[HttpPost(\"forgot-password\")]",
            authController,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[HttpPost(\"reset-password\")]",
            authController,
            StringComparison.Ordinal);
    }

    private static RegisterRequest ValidRegistration() => new(
        "Manoj Shekapure",
        "user@example.com",
        "abc123",
        "9876543210",
        true);

    private static User NewUser(
        string email = "user@example.com",
        string phone = "+919876543210",
        Guid? roleId = null,
        string roleName = "Candidate") => new()
        {
            Email = email,
            NormalizedEmail = email.ToLowerInvariant(),
            NormalizedPhoneNumber = phone,
            PhoneNumber = phone,
            PhoneConfirmed = true,
            PasswordHash = HashPassword("abc123"),
            FirstName = "Manoj",
            LastName = "Shekapure",
            Status = UserStatus.Active,
            RoleId = roleId ?? SystemRoleIds.Candidate,
            Role = new Role
            {
                Id = roleId ?? SystemRoleIds.Candidate,
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            }
        };

    private static Fixture CreateFixture(params string[] otpValues)
    {
        var time = new MutableTimeProvider(Now);
        var users = new UserRepositoryFake();
        var challengeRepository = new ChallengeRepositoryFake();
        var refreshTokens = new RefreshTokenRepositoryFake(users);
        var unitOfWork = new UnitOfWorkFake();
        var passwords = new PasswordHasherFake();
        var otp = new OtpServiceFake(otpValues);
        var sms = new SmsServiceFake();
        var email = new EmailServiceFake();
        var audit = new AuditWriterTestDouble();
        var logger = new TestLogger<AuthService>();
        var applicationShutdown = new ApplicationShutdownFake();
        var service = new AuthService(
            users,
            challengeRepository,
            refreshTokens,
            unitOfWork,
            passwords,
            new JwtTokenServiceFake(time),
            otp,
            sms,
            email,
            audit,
            new RegisterRequestValidator(),
            new VerifyRegistrationOtpRequestValidator(),
            new ResendRegistrationOtpRequestValidator(),
            new LoginRequestValidator(),
            new RequestLoginOtpRequestValidator(),
            new LoginWithOtpRequestValidator(),
            new RequestPasswordResetRequestValidator(),
            new CompletePasswordResetRequestValidator(),
            new RefreshTokenRequestValidator(),
            new ChangePasswordRequestValidator(),
            time,
            applicationShutdown,
            logger);
        return new(
            service,
            users,
            challengeRepository,
            refreshTokens,
            unitOfWork,
            passwords,
            sms,
            email,
            audit,
            time,
            applicationShutdown,
            logger);
    }

    private sealed record Fixture(
        AuthService Service,
        UserRepositoryFake Users,
        ChallengeRepositoryFake Challenges,
        RefreshTokenRepositoryFake RefreshTokens,
        UnitOfWorkFake UnitOfWork,
        PasswordHasherFake Passwords,
        SmsServiceFake Sms,
        EmailServiceFake Email,
        AuditWriterTestDouble Audit,
        MutableTimeProvider Time,
        ApplicationShutdownFake ApplicationShutdown,
        TestLogger<AuthService> Logger);

    private sealed class UserRepositoryFake : IUserRepository
    {
        public List<User> Items { get; } = [];

        public Task<User?> GetByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(
                user => user.NormalizedEmail == normalizedEmail));

        public Task<User?> GetByNormalizedPhoneAsync(
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(
                user => user.NormalizedPhoneNumber == normalizedPhoneNumber));

        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail,
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(user =>
                user.NormalizedEmail == normalizedEmail ||
                user.NormalizedPhoneNumber == normalizedPhoneNumber));

        public Task<User?> GetByIdWithRoleAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(user => user.Id == userId));

        public Task AddAsync(
            User user,
            CancellationToken cancellationToken = default)
        {
            Items.Add(user);
            return Task.CompletedTask;
        }

        public void Update(User user)
        {
        }
    }

    private sealed class ChallengeRepositoryFake :
        IAuthenticationChallengeRepository
    {
        public List<PendingRegistration> Pending { get; } = [];
        public List<OtpChallenge> OtpChallenges { get; } = [];

        public Task<PendingRegistration?> GetPendingByIdentityAsync(
            string normalizedEmail,
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Pending.SingleOrDefault(item =>
                item.ClosedAtUtc is null &&
                item.NormalizedEmail == normalizedEmail &&
                item.NormalizedPhoneNumber == normalizedPhoneNumber));

        public Task<OtpChallenge?> GetChallengeByIdAsync(
            Guid challengeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OtpChallenges.SingleOrDefault(
                challenge => challenge.Id == challengeId));

        public Task<OtpChallenge?> GetLatestForPhoneAsync(
            string normalizedPhoneNumber,
            OtpPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OtpChallenges
                .Where(challenge =>
                    challenge.NormalizedPhoneNumber == normalizedPhoneNumber &&
                    challenge.Purpose == purpose)
                .OrderByDescending(challenge => challenge.LastSentAtUtc)
                .ThenByDescending(challenge => challenge.Id)
                .FirstOrDefault());

        public Task<int> CountSentSinceAsync(
            string normalizedPhoneNumber,
            OtpPurpose purpose,
            DateTime sinceUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OtpChallenges
                .Where(challenge =>
                    challenge.NormalizedPhoneNumber == normalizedPhoneNumber &&
                    challenge.Purpose == purpose &&
                    challenge.LastSentAtUtc >= sinceUtc)
                .Sum(challenge => challenge.SendCount));

        public Task AddPendingAsync(
            PendingRegistration pendingRegistration,
            CancellationToken cancellationToken = default)
        {
            Pending.Add(pendingRegistration);
            return Task.CompletedTask;
        }

        public Task AddChallengeAsync(
            OtpChallenge challenge,
            CancellationToken cancellationToken = default)
        {
            OtpChallenges.Add(challenge);
            challenge.PendingRegistration?.OtpChallenges.Add(challenge);
            return Task.CompletedTask;
        }

        public void Update(PendingRegistration pendingRegistration)
        {
        }

        public void Update(OtpChallenge challenge)
        {
        }
    }

    private sealed class RefreshTokenRepositoryFake(
        UserRepositoryFake users) : IRefreshTokenRepository
    {
        public List<RefreshToken> Added { get; } = [];
        public bool RevokedForUser { get; private set; }

        public Task<RefreshToken?> GetByTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(token => token.Token == tokenHash));

        public Task AddAsync(
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default)
        {
            refreshToken.User = users.Items.Single(
                user => user.Id == refreshToken.UserId);
            Added.Add(refreshToken);
            return Task.CompletedTask;
        }

        public void Update(RefreshToken refreshToken)
        {
        }

        public Task RevokeActiveForUserAsync(
            Guid userId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default)
        {
            RevokedForUser = true;
            foreach (var token in Added.Where(item => item.UserId == userId))
                token.RevokedAtUtc = revokedAtUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public Exception? ExceptionToThrow { get; set; }
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return ExceptionToThrow is null
                ? Task.FromResult(1)
                : Task.FromException<int>(ExceptionToThrow);
        }
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) => HashPassword(password);

        public bool Verify(string password, string passwordHash) =>
            Hash(password) == passwordHash;
    }

    private sealed class OtpServiceFake : IOneTimePasswordService
    {
        private readonly Queue<string> values;

        public OtpServiceFake(IEnumerable<string> otpValues) =>
            values = new(otpValues.DefaultIfEmpty("123456"));

        public string Generate() => values.Count > 1
            ? values.Dequeue()
            : values.Peek();

        public string Hash(string otp) => Sha256(otp);

        public bool Verify(string otp, string expectedHash) =>
            CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Hash(otp)),
                Convert.FromHexString(expectedHash));
    }

    private sealed class SmsServiceFake : ISmsService
    {
        public string? LastOtp { get; private set; }
        public int SendCount { get; private set; }
        public Action? BeforeSend { get; set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<SmsDeliveryResult> SendOtpAsync(
            string normalizedPhoneNumber,
            string otp,
            OtpPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            BeforeSend?.Invoke();
            LastOtp = otp;
            LastCancellationToken = cancellationToken;
            SendCount++;
            return Task.FromResult(SmsDeliveryResult.Sent);
        }
    }

    private sealed class EmailServiceFake : IEmailService
    {
        public Action? BeforeSend { get; set; }
        public string? LastRawToken { get; private set; }
        public int SendCount { get; private set; }
        public User? User { get; private set; }

        public Task<EmailDeliveryResult> SendPasswordResetAsync(
            User user,
            string rawToken,
            CancellationToken cancellationToken = default)
        {
            BeforeSend?.Invoke();
            User = user;
            LastRawToken = rawToken;
            SendCount++;
            return Task.FromResult(EmailDeliveryResult.Sent);
        }

        public Task<EmailDeliveryResult> SendApplicationStatusAsync(
            User user,
            string jobTitle,
            JobApplicationStatus status,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ApplicationShutdownFake : IApplicationShutdown
    {
        public CancellationToken ApplicationStopping => default;
    }

    private sealed class TestLogger<T> : ILogger<T>
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

    private sealed class JwtTokenServiceFake(
        TimeProvider timeProvider) : IJwtTokenService
    {
        private int tokenNumber;

        public AccessTokenResult CreateAccessToken(User user) =>
            new("access-token", timeProvider.GetUtcNow().UtcDateTime.AddMinutes(15));

        public string GenerateRefreshToken() =>
            $"refresh-token-{++tokenNumber}";

        public string HashToken(string token) => $"hash:{token}";
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTime current = utcNow;

        public void Advance(TimeSpan duration) => current = current.Add(duration);

        public override DateTimeOffset GetUtcNow() => new(current);
    }

    private static string HashPassword(string password) => Sha256(password);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}

public sealed class AuthenticationPersistenceAndCryptoTests
{
    [Fact]
    public void ModelDefinesChallengeIndexesAndPendingIdentityUniqueness()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new JobPortalDbContext(options);
        var pending = context.Model.FindEntityType(typeof(PendingRegistration))!;
        var challenge = context.Model.FindEntityType(typeof(OtpChallenge))!;

        Assert.Contains(pending.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PendingRegistration.NormalizedEmail)]));
        Assert.Contains(pending.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PendingRegistration.NormalizedPhoneNumber)]));
        Assert.Contains(challenge.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [
                    nameof(OtpChallenge.NormalizedPhoneNumber),
                    nameof(OtpChallenge.Purpose),
                    nameof(OtpChallenge.ConsumedAtUtc),
                    nameof(OtpChallenge.ExpiresAtUtc)
                ]));
    }

    [Fact]
    public void MigrationNormalizesExistingEmailsAndConfirmsCandidatePhonesOnly()
    {
        var root = FindRepositoryRoot();
        var migration = Directory.GetFiles(
                Path.Combine(root, "JobPortal.Persistence", "Migrations"),
                "*_AddSecureMobileOtpAuthentication.cs")
            .Single();
        var source = File.ReadAllText(migration);

        Assert.Contains(
            "LOWER(LTRIM(RTRIM([Email])))",
            source,
            StringComparison.Ordinal);
        Assert.Contains("[PhoneConfirmed]", source, StringComparison.Ordinal);
        Assert.Contains(
            SystemRoleIds.Candidate.ToString(),
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            SystemRoleIds.Administrator.ToString(),
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionOtpHasherNeverStoresPlainCode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otp:HashKey"] = "test-only-otp-hash-key-with-at-least-32-characters"
            })
            .Build();
        var service = new HmacOneTimePasswordService(configuration);

        var hash = service.Hash("123456");

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain("123456", hash, StringComparison.Ordinal);
        Assert.True(service.Verify("123456", hash));
        Assert.False(service.Verify("654321", hash));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
