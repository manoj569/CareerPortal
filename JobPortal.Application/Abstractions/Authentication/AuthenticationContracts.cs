using JobPortal.Application.Features.Authentication;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Authentication;

public interface IAuthService
{
    Task<AuthenticationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthenticationResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthenticationResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(Guid userId, LogoutRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(User user);
    string GenerateRefreshToken();
    string HashToken(string token);
}

public interface IEmailService
{
    Task SendPasswordResetAsync(User user, string resetToken, CancellationToken cancellationToken = default);
}
