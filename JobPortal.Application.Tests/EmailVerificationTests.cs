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

    [Fact]
    public async Task RegistrationCreatesUnverifiedCandidateWithoutAuthenticationTokens()
    {
        var fixture = CreateFixture();

        var response = await fixture.Service.RegisterAsync(
            new("candidate@portal.test", "Strong!Password9", "Avery", "Patel", null));

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

    private static Task<RegistrationResponse> RegisterAsync(Fixture fixture) =>
        fixture.Service.RegisterAsync(
            new("candidate@portal.test", "Strong!Password9", "Avery", "Patel", null));

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
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(++SaveCount);
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";
        public bool Verify(string password, string passwordHash) => passwordHash == $"hash:{password}";
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
