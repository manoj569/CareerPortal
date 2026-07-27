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
    IValidator<RefreshTokenRequest> refreshValidator,
    IValidator<ForgotPasswordRequest> forgotPasswordValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    TimeProvider timeProvider) : IAuthService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);

    public async Task<AuthenticationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);
        var normalizedEmail = NormalizeEmail(request.Email);

        if (await users.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            throw new ConflictException("An account with that email address already exists.");
        }

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Status = UserStatus.Active,
            RoleId = SystemRoleIds.Candidate,
            Role = new Role { Id = SystemRoleIds.Candidate, Name = "Candidate", NormalizedName = "CANDIDATE" }
        };

        await users.AddAsync(user, cancellationToken);
        var response = await IssueTokensAsync(user, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Email), cancellationToken);

        if (user is null || user.Status != UserStatus.Active || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email address or password.");
        }

        user.LastLoginAtUtc = UtcNow;
        users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthenticationResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);
        var existingToken = await refreshTokens.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existingToken is null || existingToken.RevokedAtUtc is not null || existingToken.ExpiresAtUtc <= UtcNow || existingToken.User.Status != UserStatus.Active)
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
        await emailService.SendPasswordResetAsync(user, token, cancellationToken);
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
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
