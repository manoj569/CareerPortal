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

public sealed class AuthenticationTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RegistrationCreatesActiveConfirmedCandidateWithoutEmailOrTokens()
    {
        var fixture = CreateFixture();

        var response = await RegisterAsync(fixture);

        Assert.Equal(
            "Account created successfully. Please log in.",
            response.Message);
        Assert.Equal(SystemRoleIds.Candidate, fixture.Users.User!.RoleId);
        Assert.Equal(UserStatus.Active, fixture.Users.User.Status);
        Assert.True(fixture.Users.User.EmailConfirmed);
        Assert.Null(fixture.Users.User.EmailVerificationTokenHash);
        Assert.Null(fixture.Users.User.EmailVerificationTokenExpiresAtUtc);
        Assert.Null(fixture.Users.User.EmailVerificationSentAtUtc);
        Assert.Equal(0, fixture.Email.TotalSendCount);
        Assert.Empty(fixture.RefreshTokens.Added);
        Assert.Equal(["Message"], typeof(RegistrationResponse)
            .GetProperties()
            .Select(property => property.Name));
    }

    [Fact]
    public async Task NewlyRegisteredCandidateCanLoginAndRefreshImmediately()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);

        var login = await fixture.Service.LoginAsync(
            new("candidate@portal.test", "Strong!Password9"),
            "127.0.0.1");
        var refresh = await fixture.Service.RefreshAsync(
            new(login.RefreshToken),
            "127.0.0.1");

        Assert.Equal("access-token", login.AccessToken);
        Assert.Equal("access-token", refresh.AccessToken);
        Assert.Equal(SystemRoleIds.Candidate, fixture.Users.User!.RoleId);
        Assert.Equal(2, fixture.RefreshTokens.Added.Count);
        Assert.NotNull(fixture.RefreshTokens.Added[0].RevokedAtUtc);
    }

    [Fact]
    public async Task ActiveLegacyCandidateCanLoginAndRefreshRegardlessOfHistoricalFlag()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);
        fixture.Users.User!.EmailConfirmed = false;

        var login = await fixture.Service.LoginAsync(
            new("candidate@portal.test", "Strong!Password9"),
            null);
        var refresh = await fixture.Service.RefreshAsync(
            new(login.RefreshToken),
            null);

        Assert.Equal("access-token", refresh.AccessToken);
    }

    [Fact]
    public async Task InactiveCandidateCannotLoginOrRefresh()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);
        fixture.Users.User!.Status = UserStatus.Suspended;

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.LoginAsync(
                new("candidate@portal.test", "Strong!Password9"),
                null));

        fixture.Users.User.Status = UserStatus.Active;
        var login = await fixture.Service.LoginAsync(
            new("candidate@portal.test", "Strong!Password9"),
            null);
        fixture.Users.User.Status = UserStatus.Inactive;
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.RefreshAsync(new(login.RefreshToken), null));
    }

    [Fact]
    public async Task PasswordResetEmailAndSingleUseHashedTokenStillWork()
    {
        var fixture = CreateFixture();
        await RegisterAsync(fixture);

        await fixture.Service.ForgotPasswordAsync(
            new("candidate@portal.test"));

        Assert.Equal(1, fixture.Email.PasswordResetSendCount);
        Assert.NotEqual(
            fixture.Email.LastResetToken,
            fixture.Users.User!.PasswordResetTokenHash);
        await fixture.Service.ResetPasswordAsync(
            new(
                "candidate@portal.test",
                fixture.Email.LastResetToken!,
                "New!StrongPassword8"));
        Assert.Null(fixture.Users.User.PasswordResetTokenHash);
        Assert.Null(fixture.Users.User.PasswordResetTokenExpiresAtUtc);
        Assert.True(
            new PasswordHasherFake().Verify(
                "New!StrongPassword8",
                fixture.Users.User.PasswordHash));
    }

    [Theory]
    [InlineData("9876543210")]
    [InlineData("09876543210")]
    [InlineData("91 98765 43210")]
    [InlineData("+91-98765-43210")]
    [InlineData("+91 (98765) 43210")]
    public async Task RegistrationNormalizesSupportedIndianMobileFormats(
        string mobile)
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
        Assert.Equal("Avery", fixture.Users.User.FirstName);
        Assert.Equal("Patel", fixture.Users.User.LastName);
        Assert.Equal("+919876543210", fixture.Users.User.PhoneNumber);
        Assert.Equal("+919876543210", fixture.Users.User.NormalizedPhoneNumber);
        Assert.Equal(Now, fixture.Users.User.TermsAndPrivacyAcceptedAtUtc);
        Assert.Equal(UserStatus.Active, fixture.Users.User.Status);
        Assert.True(fixture.Users.User.EmailConfirmed);
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
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRequest() with { PhoneNumber = mobile });

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == "PhoneNumber");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("candidate@")]
    [InlineData("@portal.test")]
    public async Task RegistrationRejectsInvalidEmailValues(string email)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRequest() with { Email = email });

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == "Email");
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
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRequest() with
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Password = password
            });

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == "Password");
    }

    [Theory]
    [InlineData("short!A1")]
    [InlineData("alllowercase!123")]
    [InlineData("ALLUPPERCASE!123")]
    [InlineData("NoNumbers!Here")]
    [InlineData("NoSymbols123Here")]
    public async Task RegistrationPreservesPasswordPolicy(string password)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRequest() with { Password = password });

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == "Password");
    }

    [Fact]
    public async Task DuplicateEmailAndMobileResponsesRemainIdenticalAndPrivate()
    {
        var fixture = CreateFixture();
        var first = await RegisterAsync(fixture);
        var duplicateEmail = await fixture.Service.RegisterAsync(
            ValidRequest() with
            {
                Email = " CANDIDATE@PORTAL.TEST ",
                FirstName = "Jordan",
                LastName = "Singh",
                PhoneNumber = "+919123456780"
            });
        var duplicateMobile = await fixture.Service.RegisterAsync(
            ValidRequest() with
            {
                Email = "other@portal.test",
                FirstName = "Jordan",
                LastName = "Singh",
                PhoneNumber = "09876543210"
            });

        Assert.Equal(first.Message, duplicateEmail.Message);
        Assert.Equal(first.Message, duplicateMobile.Message);
        Assert.Equal(0, fixture.Email.TotalSendCount);
        var responseJson = JsonSerializer.Serialize(duplicateMobile);
        Assert.DoesNotContain(
            "candidate@portal.test",
            responseJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9876543210", responseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentIdentityConflictReturnsGenericResponseWithoutLeak()
    {
        var fixture = CreateFixture();
        fixture.UnitOfWork.ExceptionToThrow =
            new UniqueConstraintException("duplicate");
        const string password = "Strong!Password9";

        var response = await fixture.Service.RegisterAsync(ValidRequest());

        Assert.Equal(
            "Account created successfully. Please log in.",
            response.Message);
        Assert.Equal(0, fixture.Email.TotalSendCount);
        Assert.DoesNotContain(
            password,
            fixture.Users.User!.PasswordHash,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationRequiresSafeNamesAndExplicitConsent()
    {
        var validator = new RegisterRequestValidator();
        var numericFirstName = await validator.ValidateAsync(
            ValidRequest() with { FirstName = "12345" });
        var controlLastName = await validator.ValidateAsync(
            ValidRequest() with { LastName = "Patel\u0001" });
        var noConsent = await validator.ValidateAsync(
            ValidRequest() with { HasAcceptedTermsAndPrivacy = false });

        Assert.Contains(
            numericFirstName.Errors,
            error => error.PropertyName == "FirstName");
        Assert.Contains(
            controlLastName.Errors,
            error => error.PropertyName == "LastName");
        Assert.Contains(
            noConsent.Errors,
            error => error.PropertyName == "HasAcceptedTermsAndPrivacy");
    }

    [Fact]
    public void RegistrationRejectsPrivilegedAndInternalOverPosting()
    {
        const string json =
            """
            {
              "email":"candidate@portal.test",
              "password":"Strong!Password9",
              "firstName":"Avery",
              "lastName":"Patel",
              "phoneNumber":"+919876543210",
              "hasAcceptedTermsAndPrivacy":true,
              "role":"Administrator",
              "emailConfirmed":false
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RegisterRequest>(json, WebJson));
    }

    private static RegisterRequest ValidRequest() =>
        new(
            "candidate@portal.test",
            "Strong!Password9",
            "Avery",
            "Patel",
            "+919876543210",
            true);

    private static Task<RegistrationResponse> RegisterAsync(Fixture fixture) =>
        fixture.Service.RegisterAsync(ValidRequest());

    private static Fixture CreateFixture()
    {
        var users = new UserRepositoryFake();
        var refreshTokens = new RefreshTokenRepositoryFake(users);
        var unitOfWork = new CountingUnitOfWork();
        var email = new EmailServiceFake();
        var service = new AuthService(
            users,
            refreshTokens,
            unitOfWork,
            new PasswordHasherFake(),
            new JwtTokenServiceFake(),
            email,
            new RegisterRequestValidator(),
            new LoginRequestValidator(),
            new RefreshTokenRequestValidator(),
            new ForgotPasswordRequestValidator(),
            new ResetPasswordRequestValidator(),
            new ChangePasswordRequestValidator(),
            new FixedTimeProvider(Now));
        return new(service, users, refreshTokens, unitOfWork, email);
    }

    private sealed record Fixture(
        AuthService Service,
        UserRepositoryFake Users,
        RefreshTokenRepositoryFake RefreshTokens,
        CountingUnitOfWork UnitOfWork,
        EmailServiceFake Email);

    private sealed class UserRepositoryFake : IUserRepository
    {
        public User? User { get; private set; }

        public Task<User?> GetByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                User?.NormalizedEmail == normalizedEmail ? User : null);

        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail,
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                User is not null &&
                (User.NormalizedEmail == normalizedEmail ||
                    User.NormalizedPhoneNumber == normalizedPhoneNumber));

        public Task<User?> GetByIdWithRoleAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(User?.Id == userId ? User : null);

        public Task AddAsync(
            User user,
            CancellationToken cancellationToken = default)
        {
            User = user;
            return Task.CompletedTask;
        }

        public void Update(User user) => User = user;
    }

    private sealed class RefreshTokenRepositoryFake(
        UserRepositoryFake users) : IRefreshTokenRepository
    {
        public List<RefreshToken> Added { get; } = [];

        public Task<RefreshToken?> GetByTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Added.FirstOrDefault(
                    token => token.Token == tokenHash &&
                        token.RevokedAtUtc is null));

        public Task AddAsync(
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default)
        {
            refreshToken.User = users.User!;
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
            foreach (var token in Added.Where(
                token => token.UserId == userId && token.RevokedAtUtc is null))
            {
                token.RevokedAtUtc = revokedAtUtc;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
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
            Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(password)));

        public bool Verify(string password, string passwordHash) =>
            passwordHash == Hash(password);
    }

    private sealed class JwtTokenServiceFake : IJwtTokenService
    {
        private int refreshTokenNumber;

        public AccessTokenResult CreateAccessToken(User user) =>
            new("access-token", Now.AddMinutes(15));

        public string GenerateRefreshToken() =>
            $"refresh-token-{++refreshTokenNumber}";

        public string HashToken(string token) => $"hash:{token}";
    }

    private sealed class EmailServiceFake : IEmailService
    {
        public string? LastResetToken { get; private set; }
        public int PasswordResetSendCount { get; private set; }
        public int ApplicationStatusSendCount { get; private set; }
        public int TotalSendCount =>
            PasswordResetSendCount + ApplicationStatusSendCount;

        public Task<EmailDeliveryResult> SendPasswordResetAsync(
            User user,
            string resetToken,
            CancellationToken cancellationToken = default)
        {
            LastResetToken = resetToken;
            PasswordResetSendCount++;
            return Task.FromResult(EmailDeliveryResult.Sent);
        }

        public Task<EmailDeliveryResult> SendApplicationStatusAsync(
            User user,
            string jobTitle,
            JobApplicationStatus status,
            CancellationToken cancellationToken = default)
        {
            ApplicationStatusSendCount++;
            return Task.FromResult(EmailDeliveryResult.Sent);
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed class RemovedEmailVerificationContractTests
{
    [Fact]
    public void VerificationRoutesPolicyAndContractsAreAbsent()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.API",
            "Controllers",
            "AuthController.cs"));
        var program = File.ReadAllText(
            Path.Combine(root, "JobPortal.API", "Program.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.Application",
            "Abstractions",
            "Authentication",
            "AuthenticationContracts.cs"));

        Assert.DoesNotContain(
            "verify-email",
            controller,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "resend-verification",
            controller,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "EmailVerification",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "SendPasswordResetAsync",
            contracts,
            StringComparison.Ordinal);
        Assert.Contains(
            "SendApplicationStatusAsync",
            contracts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityMigrationUpdatesCandidatesOnlyAndClearsTokens()
    {
        var root = FindRepositoryRoot();
        var migration = Directory.GetFiles(
                Path.Combine(root, "JobPortal.Persistence", "Migrations"),
                "*_RemoveCandidateEmailVerificationRequirement.cs")
            .Single();
        var source = File.ReadAllText(migration);

        Assert.Contains(
            "[EmailConfirmed] = CAST(1 AS bit)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("[Status] = 2", source, StringComparison.Ordinal);
        Assert.Contains(
            "[EmailVerificationTokenHash] = NULL",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[EmailVerificationTokenExpiresAtUtc] = NULL",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[EmailVerificationSentAtUtc] = NULL",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[RoleId] = '3ec6976c-8752-48f5-a14f-1c81b6522c5d'",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            SystemRoleIds.Administrator.ToString(),
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Repository root was not found.");
    }
}
