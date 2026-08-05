using System.Security.Claims;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Authentication;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[EnableRateLimiting("Authentication")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("RegistrationOtp")]
    [ProducesResponseType(typeof(RegistrationChallengeResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<RegistrationChallengeResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return Accepted(response);
    }

    [HttpPost("verify-registration-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("OtpVerification")]
    [ProducesResponseType(typeof(RegistrationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RegistrationResponse>> VerifyRegistrationOtp(
        VerifyRegistrationOtpRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.VerifyRegistrationOtpAsync(
            request,
            cancellationToken));

    [HttpPost("resend-registration-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("RegistrationOtp")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MessageResponse>> ResendRegistrationOtp(
        ResendRegistrationOtpRequest request,
        CancellationToken cancellationToken) =>
        Accepted(await authService.ResendRegistrationOtpAsync(
            request,
            cancellationToken));

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthenticationResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));

    [HttpPost("request-login-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("OtpRequest")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MessageResponse>> RequestLoginOtp(
        RequestLoginOtpRequest request,
        CancellationToken cancellationToken) =>
        Accepted(await authService.RequestLoginOtpAsync(
            request,
            cancellationToken));

    [HttpPost("login-with-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("OtpVerification")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticationResponse>> LoginWithOtp(
        LoginWithOtpRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.LoginWithOtpAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticationResponse>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.RefreshAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));

    [HttpPost("request-password-reset")]
    [AllowAnonymous]
    [EnableRateLimiting("OtpRequest")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<MessageResponse>> RequestPasswordReset(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken) =>
        Accepted(await authService.RequestPasswordResetAsync(
            request,
            cancellationToken));

    [HttpPost("complete-password-reset")]
    [AllowAnonymous]
    [EnableRateLimiting("OtpVerification")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MessageResponse>> CompletePasswordReset(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken) =>
        Ok(await authService.CompletePasswordResetAsync(
            request,
            cancellationToken));

    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await authService.ChangePasswordAsync(GetUserId(), request, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(GetUserId(), request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Administrator")]
    [HttpGet("admin/health")]
    public IActionResult AdministratorHealth() => Ok(new { message = "Administrator access granted." });

    private Guid GetUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new UnauthorizedException();
}
