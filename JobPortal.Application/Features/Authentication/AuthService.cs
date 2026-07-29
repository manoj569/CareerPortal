using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Authentication;

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IEmailService emailService,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<VerifyEmailRequest> verifyEmailValidator,
    IValidator<ResendVerificationRequest> resendVerificationValidator,
    IValidator<RefreshTokenRequest> refreshValidator,
    IValidator<ForgotPasswordRequest> forgotPasswordValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    TimeProvider timeProvider) : IAuthService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);
    private const string VerificationRequiredMessage =
        "Registration accepted. Verify your email address before signing in.";
    private const string ResendMessage =
        "If an unverified account exists, a verification message will be sent.";

    public async Task<RegistrationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);
        var normalizedEmail = NormalizeEmail(request.Email);

        if (await users.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            return new RegistrationResponse(VerificationRequiredMessage);
        }

        var token = GenerateSecureToken();
        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Status = UserStatus.Pending,
            EmailConfirmed = false,
            EmailVerificationTokenHash = HashToken(token),
            EmailVerificationTokenExpiresAtUtc = UtcNow.Add(EmailVerificationLifetime),
            EmailVerificationSentAtUtc = UtcNow,
            RoleId = SystemRoleIds.Candidate,
            Role = new Role { Id = SystemRoleIds.Candidate, Name = "Candidate", NormalizedName = "CANDIDATE" }
        };

        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _ = await emailService.SendEmailVerificationAsync(user, token, cancellationToken);
        return new RegistrationResponse(VerificationRequiredMessage);
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Email), cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email address or password.");
        }
        if (!user.EmailConfirmed) throw new EmailNotVerifiedException();
        if (user.Status != UserStatus.Active)
            throw new UnauthorizedException("Invalid email address or password.");

        user.LastLoginAtUtc = UtcNow;
        users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<VerificationResponse> VerifyEmailAsync(
        VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        await verifyEmailValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        var suppliedHash = HashToken(request.Token);
        if (user is null || user.EmailConfirmed || user.EmailVerificationTokenHash is null ||
            user.EmailVerificationTokenExpiresAtUtc <= UtcNow ||
            !FixedTimeEquals(user.EmailVerificationTokenHash, suppliedHash))
            throw new BadRequestException("The email verification token is invalid or expired.", "invalid_verification_token");

        user.EmailConfirmed = true;
        user.Status = UserStatus.Active;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAtUtc = null;
        users.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new VerificationResponse("Email verified successfully.");
    }

    public async Task<VerificationResponse> ResendVerificationAsync(
        ResendVerificationRequest request, CancellationToken cancellationToken = default)
    {
        await resendVerificationValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        if (user is null || user.EmailConfirmed || user.RoleId != SystemRoleIds.Candidate)
            return new VerificationResponse(ResendMessage);

        var token = GenerateSecureToken();
        user.EmailVerificationTokenHash = HashToken(token);
        user.EmailVerificationTokenExpiresAtUtc = UtcNow.Add(EmailVerificationLifetime);
        user.EmailVerificationSentAtUtc = UtcNow;
        users.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _ = await emailService.SendEmailVerificationAsync(user, token, cancellationToken);
        return new VerificationResponse(ResendMessage);
    }

    public async Task<AuthenticationResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);
        var existingToken = await refreshTokens.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || existingToken.RevokedAtUtc is not null ||
            existingToken.ExpiresAtUtc <= UtcNow || existingToken.User.Status != UserStatus.Active ||
            !existingToken.User.EmailConfirmed)
        {
            throw new UnauthorizedException("The refresh token is invalid or expired.");
        }

        var response = await IssueTokensAsync(existingToken.User, ipAddress, cancellationToken);
        existingToken.RevokedAtUtc = UtcNow;
        existingToken.RevokedByIp = ipAddress;
        existingToken.ReplacedByToken = jwtTokenService.HashToken(response.RefreshToken);
        refreshTokens.Update(existingToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        await forgotPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Email), cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            return;
        }

        var token = jwtTokenService.GenerateRefreshToken();
        user.PasswordResetTokenHash = jwtTokenService.HashToken(token);
        user.PasswordResetTokenExpiresAtUtc = UtcNow.Add(PasswordResetLifetime);
        users.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _ = await emailService.SendPasswordResetAsync(user, token, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        await resetPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        var resetTokenHash = jwtTokenService.HashToken(request.Token);

        if (user is null || user.PasswordResetTokenExpiresAtUtc <= UtcNow || user.PasswordResetTokenHash is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.PasswordResetTokenHash), Encoding.UTF8.GetBytes(resetTokenHash)))
        {
            throw new BadRequestException("The password reset token is invalid or expired.", "invalid_reset_token");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAtUtc = null;
        users.Update(user);
        await refreshTokens.RevokeActiveForUserAsync(user.Id, UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByIdWithRoleAsync(userId, cancellationToken) ?? throw new UnauthorizedException();

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("The current password is incorrect.", "invalid_current_password");
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        users.Update(user);
        await refreshTokens.RevokeActiveForUserAsync(userId, UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAsync(Guid userId, LogoutRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(new RefreshTokenRequest(request.RefreshToken), cancellationToken);
        var token = await refreshTokens.GetByTokenHashAsync(jwtTokenService.HashToken(request.RefreshToken), cancellationToken);

        if (token is null || token.UserId != userId || token.RevokedAtUtc is not null)
        {
            return;
        }

        token.RevokedAtUtc = UtcNow;
        token.RevokedByIp = ipAddress;
        refreshTokens.Update(token);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthenticationResponse> IssueTokensAsync(User user, string? ipAddress, CancellationToken cancellationToken)
    {
        var accessToken = jwtTokenService.CreateAccessToken(user);
        var rawRefreshToken = jwtTokenService.GenerateRefreshToken();
        var refreshToken = new RefreshToken
        {
            Token = jwtTokenService.HashToken(rawRefreshToken),
            ExpiresAtUtc = UtcNow.Add(RefreshTokenLifetime),
            CreatedByIp = ipAddress,
            UserId = user.Id
        };

        await refreshTokens.AddAsync(refreshToken, cancellationToken);
        return new AuthenticationResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshToken.ExpiresAtUtc,
            new AuthenticatedUserDto(user.Id, user.Email, user.FirstName, user.LastName, user.Role.Name));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expected), Convert.FromHexString(actual));
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
