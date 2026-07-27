namespace JobPortal.Application.Features.Authentication;

public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName, string? PhoneNumber);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record LogoutRequest(string RefreshToken);
public sealed record AuthenticatedUserDto(Guid Id, string Email, string FirstName, string LastName, string Role);
public sealed record AuthenticationResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, AuthenticatedUserDto User);
