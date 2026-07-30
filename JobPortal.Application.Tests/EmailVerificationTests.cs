using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Authentication;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class EmailVerificationTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RegistrationCreatesUnverifiedCandidateWithoutAuthenticationTokens()
    {
        var fixture = CreateFixture();

        var response = await fixture.Service.RegisterAsync(
            new(
                "candidate@portal.test",
                "Strong!Password9",
                "Avery",
                "Patel",
                "+919876543210",
                true));

        Assert.Contains("Verify", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SystemRoleIds.Candidate, fixture.Users.User!.RoleId);
        Assert.Equal(UserStatus.Pending, fixture.Users.User.Status);
        Assert.False(fixture.Users.User.EmailConfirmed);
        Assert.NotEqual(fixture.Email.LastToken, fixture.Users.User.EmailVerificationTokenHash);
        Assert.Equal(64, fixture.Users.User.EmailVerificationTokenHash!.Length);
        Assert.Empty(fixture.RefreshTokens.Added);
    }

    [Fact]
    public async Task ValidTokenVerifiesActivatesAndCannotBeReused()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);
        var request = new VerifyEmailRequest("candidate@portal.test", fixture.Email.LastToken!);

        await fixture.Service.VerifyEmailAsync(request);

        Assert.True(fixture.Users.User!.EmailConfirmed);
        Assert.Equal(UserStatus.Active, fixture.Users.User.Status);
        Assert.Null(fixture.Users.User.EmailVerificationTokenHash);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.VerifyEmailAsync(request));
    }

    [Fact]
    public async Task InvalidAndExpiredTokensAreRejected()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.VerifyEmailAsync(
            new("candidate@portal.test", "invalid-token")));
        fixture.Users.User!.EmailVerificationTokenExpiresAtUtc = Now.AddSeconds(-1);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.VerifyEmailAsync(
            new("candidate@portal.test", fixture.Email.LastToken!)));
    }

    [Fact]
    public async Task UnverifiedLoginReturnsMachineReadableErrorAndVerifiedLoginSucceeds()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);

        var exception = await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            fixture.Service.LoginAsync(new("candidate@portal.test", "Strong!Password9"), null));
        Assert.Equal("email_not_verified", exception.Code);

        fixture.Users.User!.EmailConfirmed = true;
        fixture.Users.User.Status = UserStatus.Active;
        var response = await fixture.Service.LoginAsync(
            new("candidate@portal.test", "Strong!Password9"), null);
        Assert.Equal("access-token", response.AccessToken);
    }

    [Fact]
    public async Task ResendIsPrivacySafeRotatesTokenAndSkipsVerifiedAccounts()
    {
        var fixture = CreateFixture();
        var missingResponse = await fixture.Service.ResendVerificationAsync(new("missing@portal.test"));
        await RegisterAsync(fixture);
        var firstToken = fixture.Email.LastToken;
        var response = await fixture.Service.ResendVerificationAsync(new("candidate@portal.test"));

        Assert.Equal(missingResponse.Message, response.Message);
        Assert.NotEqual(firstToken, fixture.Email.LastToken);
        var sendCount = fixture.Email.VerificationSendCount;
        fixture.Users.User!.EmailConfirmed = true;
        await fixture.Service.ResendVerificationAsync(new("candidate@portal.test"));
        Assert.Equal(sendCount, fixture.Email.VerificationSendCount);
    }

    [Fact]
    public async Task DeliveryFailureDoesNotRollbackCommittedTokenState()
    {
        var fixture = CreateFixture();
        fixture.Email.Result = EmailDeliveryResult.Failed;

        await RegisterAsync(fixture);

        Assert.NotNull(fixture.Users.User!.EmailVerificationTokenHash);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task DuplicateRegistrationDoesNotRevealAccountExistence()
    {
        var fixture = CreateFixture();
        var first = await RegisterAsync(fixture);
        var second = await RegisterAsync(fixture);

        Assert.Equal(first.Message, second.Message);
        Assert.Equal(1, fixture.Email.VerificationSendCount);
    }

    [Fact]
    public async Task ExistingPasswordResetFlowStillUsesHashedSingleUseToken()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);
        fixture.Users.User!.EmailConfirmed = true;
        fixture.Users.User.Status = UserStatus.Active;

        await fixture.Service.ForgotPasswordAsync(new("candidate@portal.test"));
        Assert.NotEqual(fixture.Email.LastResetToken, fixture.Users.User.PasswordResetTokenHash);
        await fixture.Service.ResetPasswordAsync(
            new("candidate@portal.test", fixture.Email.LastResetToken!, "New!StrongPassword8"));
        Assert.Null(fixture.Users.User.PasswordResetTokenHash);
    }

    [Theory]
    [InlineData("9876543210")]
    [InlineData("09876543210")]
    [InlineData("91 98765 43210")]
    [InlineData("+91-98765-43210")]
    [InlineData("+91 (98765) 43210")]
    public async Task RegistrationNormalizesSupportedIndianMobileFormats(string mobile)
    {
        var fixture = CreateFixture();

        await fixture.Service.RegisterAsync(new(
            "  Candidate@Portal.Test  ",
            "Strong!Password9",
            " Avery ",
            " Patel ",
            mobile,
            true));

        Assert.Equal("Candidate@Portal.Test", fixture.Users.User!.Email);
        Assert.Equal("CANDIDATE@PORTAL.TEST", fixture.Users.User.NormalizedEmail);
        Assert.Equal("+919876543210", fixture.Users.User.PhoneNumber);
        Assert.Equal("+919876543210", fixture.Users.User.NormalizedPhoneNumber);
        Assert.Equal(Now, fixture.Users.User.TermsAndPrivacyAcceptedAtUtc);
        Assert.Equal(SystemRoleIds.Candidate, fixture.Users.User.RoleId);
        Assert.Equal(UserStatus.Pending, fixture.Users.User.Status);
        Assert.False(fixture.Users.User.EmailConfirmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+15551234567")]
    [InlineData("+915876543210")]
    [InlineData("+919999999999")]
    [InlineData("+919876598765")]
    [InlineData("+91-98--76543210")]
    [InlineData("+91987654321")]
    [InlineData("+919876543210x12")]
    [InlineData("+91/9876543210")]
    public async Task RegistrationRejectsInvalidMobileValues(string mobile)
    {
        var validator = new RegisterRequestValidator();

        var result = await validator.ValidateAsync(new RegisterRequest(
            "candidate@portal.test",
            "Strong!Password9",
            "Avery",
            "Patel",
            mobile,
            true));

        Assert.Contains(result.Errors, error => error.PropertyName == "PhoneNumber");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("candidate@")]
    [InlineData("@portal.test")]
    public async Task RegistrationRejectsInvalidEmailValues(string email)
    {
        var validator = new RegisterRequestValidator();

        var result = await validator.ValidateAsync(new RegisterRequest(
            email,
            "Strong!Password9",
            "Avery",
            "Patel",
            "+919876543210",
            true));

        Assert.Contains(result.Errors, error => error.PropertyName == "Email");
    }

    [Theory]
    [InlineData("candidate@portal.test", "Avery", "Patel", "Candidate!Secure9X")]
    [InlineData("person@portal.test", "Avery", "Patel", "Avery!Secure99")]
    [InlineData("person@portal.test", "Avery", "Patel", "Patel!Secure99")]
    [InlineData("person@portal.test", "Avery", "Patel", "9876543210!Aa")]
    public async Task RegistrationRejectsPasswordsContainingPersonalData(
        string email,
        string firstName,
        string lastName,
        string password)
    {
        var validator = new RegisterRequestValidator();

        var result = await validator.ValidateAsync(new RegisterRequest(
            email,
            password,
            firstName,
            lastName,
            "+919876543210",
            true));

        Assert.Contains(result.Errors, error => error.PropertyName == "Password");
    }

    [Fact]
    public async Task DuplicateEmailAndMobileResponsesAreIdenticalAndPrivacySafe()
    {
        var fixture = CreateFixture();
        var first = await RegisterAsync(fixture);
        var duplicateEmail = await fixture.Service.RegisterAsync(new(
            " CANDIDATE@PORTAL.TEST ",
            "Secure!Credential9X",
            "Jordan",
            "Singh",
            "+919123456780",
            true));
        var duplicateMobile = await fixture.Service.RegisterAsync(new(
            "other@portal.test",
            "Secure!Credential9X",
            "Jordan",
            "Singh",
            "09876543210",
            true));

        Assert.Equal(first.Message, duplicateEmail.Message);
        Assert.Equal(first.Message, duplicateMobile.Message);
        Assert.Equal(1, fixture.Email.VerificationSendCount);
        var responseJson = JsonSerializer.Serialize(duplicateMobile);
        Assert.DoesNotContain("candidate@portal.test", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9876543210", responseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentIdentityConflictReturnsGenericResponseWithoutSendingOrLeaking()
    {
        var fixture = CreateFixture();
        fixture.UnitOfWork.ExceptionToThrow = new UniqueConstraintException("duplicate");
        const string password = "Strong!Password9";

        var response = await fixture.Service.RegisterAsync(new(
            "candidate@portal.test",
            password,
            "Avery",
            "Patel",
            "+919876543210",
            true));

        Assert.Contains("Verify", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Email.VerificationSendCount);
        Assert.DoesNotContain(
            password,
            fixture.Users.User!.PasswordHash,
            StringComparison.Ordinal);
        Assert.Equal(SystemRoleIds.Candidate, fixture.Users.User.RoleId);
    }

    [Fact]
    public async Task RegistrationRequiresSafeNamesAndExplicitConsent()
    {
        var validator = new RegisterRequestValidator();
        var numericName = await validator.ValidateAsync(new RegisterRequest(
            "candidate@portal.test",
            "Strong!Password9",
            "12345",
            "Patel",
            "+919876543210",
            true));
        var controlName = await validator.ValidateAsync(new RegisterRequest(
            "candidate@portal.test",
            "Strong!Password9",
            "Avery\u0001",
            "Patel",
            "+919876543210",
            true));
        var noConsent = await validator.ValidateAsync(new RegisterRequest(
            "candidate@portal.test",
            "Strong!Password9",
            "Avery",
            "Patel",
            "+919876543210",
            false));

        Assert.Contains(numericName.Errors, error => error.PropertyName == "FirstName");
        Assert.Contains(controlName.Errors, error => error.PropertyName == "FirstName");
        Assert.Contains(
            noConsent.Errors,
            error => error.PropertyName == "HasAcceptedTermsAndPrivacy");
    }

    [Fact]
    public void RegistrationRejectsPrivilegedAndInternalOverPosting()
    {
        var json =
            """
            {
              "email":"candidate@portal.test",
              "password":"Strong!Password9",
              "firstName":"Avery",
              "lastName":"Patel",
              "phoneNumber":"+919876543210",
              "hasAcceptedTermsAndPrivacy":true,
              "role":"Administrator",
              "emailConfirmed":true
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RegisterRequest>(
                json, WebJson));
    }

    private static Task<RegistrationResponse> RegisterAsync(Fixture fixture) =>
        fixture.Service.RegisterAsync(
            new(
                "candidate@portal.test",
                "Strong!Password9",
                "Avery",
                "Patel",
                "+919876543210",
                true));

    private static Fixture CreateFixture()
    {
        var users = new UserRepositoryFake();
        var refreshTokens = new RefreshTokenRepositoryFake();
        var unitOfWork = new CountingUnitOfWork();
        var email = new EmailServiceFake();
        var service = new AuthService(users, refreshTokens, unitOfWork,
            new PasswordHasherFake(), new JwtTokenServiceFake(), email,
            new RegisterRequestValidator(), new LoginRequestValidator(),
            new VerifyEmailRequestValidator(), new ResendVerificationRequestValidator(),
            new RefreshTokenRequestValidator(), new ForgotPasswordRequestValidator(),
            new ResetPasswordRequestValidator(), new ChangePasswordRequestValidator(),
            new FixedTimeProvider(Now));
        return new(service, users, refreshTokens, unitOfWork, email);
    }

    private sealed record Fixture(
        AuthService Service, UserRepositoryFake Users, RefreshTokenRepositoryFake RefreshTokens,
        CountingUnitOfWork UnitOfWork, EmailServiceFake Email);

    private sealed class UserRepositoryFake : IUserRepository
    {
        public User? User { get; private set; }
        public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(User?.NormalizedEmail == normalizedEmail ? User : null);
        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail,
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(User is not null &&
                (User.NormalizedEmail == normalizedEmail ||
                 User.NormalizedPhoneNumber == normalizedPhoneNumber));
        public Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(User?.Id == userId ? User : null);
        public Task AddAsync(User user, CancellationToken cancellationToken = default) { User = user; return Task.CompletedTask; }
        public void Update(User user) => User = user;
    }

    private sealed class RefreshTokenRepositoryFake : IRefreshTokenRepository
    {
        public List<RefreshToken> Added { get; } = [];
        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.SingleOrDefault(x => x.Token == tokenHash));
        public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
        {
            Added.Add(refreshToken);
            return Task.CompletedTask;
        }
        public void Update(RefreshToken refreshToken) { }
        public Task RevokeActiveForUserAsync(Guid userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return ExceptionToThrow is null
                ? Task.FromResult(SaveCount)
                : Task.FromException<int>(ExceptionToThrow);
        }
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        public bool Verify(string password, string passwordHash) =>
            passwordHash == Hash(password);
    }

    private sealed class JwtTokenServiceFake : IJwtTokenService
    {
        public AccessTokenResult CreateAccessToken(User user) => new("access-token", Now.AddMinutes(15));
        public string GenerateRefreshToken() => "refresh-token";
        public string HashToken(string token) => $"hash:{token}";
    }

    private sealed class EmailServiceFake : IEmailService
    {
        public string? LastToken { get; private set; }
        public string? LastResetToken { get; private set; }
        public int VerificationSendCount { get; private set; }
        public EmailDeliveryResult Result { get; set; } = EmailDeliveryResult.Sent;
        public Task<EmailDeliveryResult> SendEmailVerificationAsync(
            User user, string verificationToken, CancellationToken cancellationToken = default)
        {
            LastToken = verificationToken;
            VerificationSendCount++;
            return Task.FromResult(Result);
        }
        public Task<EmailDeliveryResult> SendPasswordResetAsync(
            User user, string resetToken, CancellationToken cancellationToken = default)
        {
            LastResetToken = resetToken;
            return Task.FromResult(Result);
        }
        public Task<EmailDeliveryResult> SendApplicationStatusAsync(
            User user, string jobTitle, JobApplicationStatus status,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed class EmailVerificationApiContractTests
{
    [Fact]
    public void VerificationEndpointsAndRateLimitPolicyAreDeclared()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            root, "JobPortal.API", "Controllers", "AuthController.cs"));
        var program = File.ReadAllText(Path.Combine(root, "JobPortal.API", "Program.cs"));

        Assert.Contains("[HttpPost(\"verify-email\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"resend-verification\")]", controller, StringComparison.Ordinal);
        Assert.Equal(2, Count(controller, "[EnableRateLimiting(\"EmailVerification\")]"));
        Assert.Contains("AddPolicy(\"EmailVerification\"", program, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }
}
