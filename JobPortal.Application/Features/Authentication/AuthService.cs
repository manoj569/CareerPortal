using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Legal;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobPortal.Application.Features.Authentication;

public sealed class AuthService(
    IUserRepository users,
    IAuthenticationChallengeRepository challenges,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IOneTimePasswordService otpService,
    ISmsService smsService,
    IEmailService emailService,
    IAuditWriter auditWriter,
    IValidator<RegisterRequest> registerValidator,
    IValidator<VerifyRegistrationOtpRequest> registrationOtpValidator,
    IValidator<ResendRegistrationOtpRequest> registrationResendValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<RequestLoginOtpRequest> requestLoginOtpValidator,
    IValidator<LoginWithOtpRequest> loginWithOtpValidator,
    IValidator<RequestPasswordResetRequest> requestPasswordResetValidator,
    IValidator<CompletePasswordResetRequest> completeResetValidator,
    IValidator<RefreshTokenRequest> refreshValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    TimeProvider timeProvider,
    IApplicationShutdown applicationShutdown,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly Action<ILogger, string, OtpPurpose, string, string, string, string, Exception?>
        AuthenticationInformation = LoggerMessage.Define<string, OtpPurpose, string, string, string, string>(
            LogLevel.Information,
            new EventId(1200, nameof(AuthenticationInformation)),
            "Authentication event {Category}: purpose {Purpose}, challenge {ChallengeId}, mobile suffix {LastFourDigits}, status {Status}, exception type {ExceptionType}.");

    private static readonly Action<ILogger, string, OtpPurpose, string, string, string, string, Exception?>
        AuthenticationWarning = LoggerMessage.Define<string, OtpPurpose, string, string, string, string>(
            LogLevel.Warning,
            new EventId(1201, nameof(AuthenticationWarning)),
            "Authentication event {Category}: purpose {Purpose}, challenge {ChallengeId}, mobile suffix {LastFourDigits}, status {Status}, exception type {ExceptionType}.");

    private static readonly Action<ILogger, string, OtpPurpose, string, string, string, string, Exception?>
        AuthenticationError = LoggerMessage.Define<string, OtpPurpose, string, string, string, string>(
            LogLevel.Error,
            new EventId(1202, nameof(AuthenticationError)),
            "Authentication event {Category}: purpose {Purpose}, challenge {ChallengeId}, mobile suffix {LastFourDigits}, status {Status}, exception type {ExceptionType}.");


    // Constants
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PendingRegistrationLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SendRateWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan OtpDeliveryBudget = TimeSpan.FromSeconds(20);
    private const int MaximumOtpAttempts = 5;
    private const int MaximumSendsPerWindow = 5;
    private const string RegistrationPendingMessage = "Registration request accepted. Use resend OTP if needed.";
    private const string OtpSentMessage = "If the mobile number is eligible, an OTP has been sent.";
    private const string RegistrationSuccessMessage = "Registration successful. Please log in.";
    private const string PasswordChangedMessage = "Password changed successfully. Please log in.";
    private const string PasswordResetRequestedMessage =
        "If an account exists for this email address, a password reset link has been sent.";

    public async Task<RegistrationChallengeResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var challengePersisted = false;
        LogAuthenticationEvent(
            "registration_started",
            OtpPurpose.Registration,
            request.PhoneNumber,
            status: "started");
        try
        {
            return await RegisterCoreAsync(
                request,
                () => challengePersisted = true,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested &&
                !challengePersisted)
        {
            LogAuthenticationEvent(
                "request_cancelled_before_persist",
                OtpPurpose.Registration,
                request.PhoneNumber,
                status: "cancelled",
                exception: exception,
                level: LogLevel.Warning);
            throw;
        }
    }

    private async Task<RegistrationChallengeResponse> RegisterCoreAsync(
        RegisterRequest request,
        Action markChallengePersisted,
        CancellationToken cancellationToken)
    {
        request = request with
        {
            FullName = request.FullName?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty
        };

        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);
        _ = PersonalName.TrySplit(request.FullName, out var firstName, out var lastName);
        var normalizedEmail = NormalizeEmail(request.Email);
        _ = IndianMobileNumber.TryNormalizeTenDigit(request.PhoneNumber, out var normalizedPhoneNumber);

        LogAuthenticationEvent(
            "registration_validated",
            OtpPurpose.Registration,
            normalizedPhoneNumber,
            status: "validated");

        var identityExists = await users.RegistrationIdentityExistsAsync(
            normalizedEmail,
            normalizedPhoneNumber,
            cancellationToken);
        LogAuthenticationEvent(
            "registration_identity_checked",
            OtpPurpose.Registration,
            normalizedPhoneNumber,
            status: identityExists ? "existing_user" : "available");
        if (identityExists)
        {
            LogAuthenticationEvent(
                "send_skipped_existing_user",
                OtpPurpose.Registration,
                normalizedPhoneNumber,
                status: "skipped");
            return DecoyRegistrationResponse();
        }

        if (await IsMobileSendRateExceededAsync(normalizedPhoneNumber, OtpPurpose.Registration, cancellationToken))
        {
            LogAuthenticationEvent(
                "send_skipped_rate_limit",
                OtpPurpose.Registration,
                normalizedPhoneNumber,
                status: "skipped");
            return DecoyRegistrationResponse();
        }

        var existing = await challenges.GetPendingByIdentityAsync(normalizedEmail, normalizedPhoneNumber, cancellationToken);
        if (existing is not null)
        {
            if (existing.ExpiresAtUtc <= UtcNow)
            {
                LogAuthenticationEvent(
                    "pending_registration_found",
                    OtpPurpose.Registration,
                    normalizedPhoneNumber,
                    status: "expired");
                existing.ClosedAtUtc = UtcNow;
                foreach (var oldChallenge in existing.OtpChallenges.Where(challenge => challenge.ConsumedAtUtc is null))
                {
                    oldChallenge.ConsumedAtUtc = UtcNow;
                    challenges.Update(oldChallenge);
                }
                challenges.Update(existing);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var currentChallenge = existing.OtpChallenges
                    .Where(challenge => challenge.Purpose == OtpPurpose.Registration && challenge.ConsumedAtUtc is null)
                    .OrderByDescending(challenge => challenge.LastSentAtUtc)
                    .FirstOrDefault();

                LogAuthenticationEvent(
                    "pending_registration_found",
                    OtpPurpose.Registration,
                    normalizedPhoneNumber,
                    currentChallenge?.Id,
                    currentChallenge is null ? "challenge_missing" : "active");

                if (currentChallenge is not null)
                {
                    var timeSinceLastSend = UtcNow - currentChallenge.LastSentAtUtc;
                    if (timeSinceLastSend < ResendCooldown)
                    {
                        LogAuthenticationEvent(
                            "send_skipped_cooldown",
                            OtpPurpose.Registration,
                            normalizedPhoneNumber,
                            currentChallenge.Id,
                            "skipped");
                        return new(currentChallenge.Id, RegistrationPendingMessage, currentChallenge.ExpiresAtUtc);
                    }

                    if (await IsMobileSendRateExceededAsync(normalizedPhoneNumber, OtpPurpose.Registration, cancellationToken))
                    {
                        LogAuthenticationEvent(
                            "send_skipped_rate_limit",
                            OtpPurpose.Registration,
                            normalizedPhoneNumber,
                            currentChallenge.Id,
                            "skipped");
                        return DecoyRegistrationResponse();
                    }

                    var newOtp = RotateChallenge(currentChallenge);
                    existing.ExpiresAtUtc = UtcNow.Add(PendingRegistrationLifetime);
                    challenges.Update(currentChallenge);
                    challenges.Update(existing);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    markChallengePersisted();

                    LogAuthenticationEvent(
                        "otp_challenge_persisted",
                        OtpPurpose.Registration,
                        normalizedPhoneNumber,
                        currentChallenge.Id,
                        "rotated");
                    await SendPersistedOtpAsync(
                        currentChallenge.Id,
                        normalizedPhoneNumber,
                        newOtp,
                        OtpPurpose.Registration);

                    return new(currentChallenge.Id, RegistrationPendingMessage, currentChallenge.ExpiresAtUtc);
                }
            }
        }

        var generatedOtp = otpService.Generate();

        var pending = new PendingRegistration
        {
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHasher.Hash(request.Password),
            FirstName = firstName,
            LastName = lastName,
            NormalizedPhoneNumber = normalizedPhoneNumber,
            TermsAndPrivacyAcceptedAtUtc = UtcNow,
            TermsAndPrivacyVersion = LegalDocumentCatalog.CurrentVersion,
            ExpiresAtUtc = UtcNow.Add(PendingRegistrationLifetime)
        };
        var challenge = NewChallenge(normalizedPhoneNumber, OtpPurpose.Registration, generatedOtp);
        challenge.PendingRegistration = pending;
        challenge.PendingRegistrationId = pending.Id;
        await challenges.AddPendingAsync(pending, cancellationToken);
        await challenges.AddChallengeAsync(challenge, cancellationToken);
        LogAuthenticationEvent(
            "pending_registration_created",
            OtpPurpose.Registration,
            normalizedPhoneNumber,
            challenge.Id,
            "created");
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            markChallengePersisted();
        }
        catch (UniqueConstraintException exception)
        {
            LogAuthenticationEvent(
                "registration_identity_checked",
                OtpPurpose.Registration,
                normalizedPhoneNumber,
                challenge.Id,
                "uniqueness_conflict",
                exception,
                LogLevel.Warning);
            return DecoyRegistrationResponse();
        }

        LogAuthenticationEvent(
            "otp_challenge_persisted",
            OtpPurpose.Registration,
            normalizedPhoneNumber,
            challenge.Id,
            "created");
        await SendPersistedOtpAsync(
            challenge.Id,
            normalizedPhoneNumber,
            generatedOtp,
            OtpPurpose.Registration);
        return new(challenge.Id, RegistrationPendingMessage, challenge.ExpiresAtUtc);
    }

    public async Task<RegistrationResponse> VerifyRegistrationOtpAsync(
        VerifyRegistrationOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        await registrationOtpValidator.ValidateAndThrowAsync(request, cancellationToken);
        var challenge = await challenges.GetChallengeByIdAsync(request.ChallengeId, cancellationToken);
        if (challenge is { Purpose: OtpPurpose.Registration, ConsumedAtUtc: not null, UserId: not null })
            return new(RegistrationSuccessMessage);

        var pending = challenge?.PendingRegistration;
        if (challenge is null ||
            challenge.Purpose != OtpPurpose.Registration ||
            pending is null ||
            pending.ClosedAtUtc is not null ||
            pending.ExpiresAtUtc <= UtcNow ||
            string.IsNullOrWhiteSpace(pending.PasswordHash))
            throw InvalidOtp();

        await ValidateOtpAsync(challenge, request.Otp, cancellationToken);
        var user = new User
        {
            Email = pending.Email,
            NormalizedEmail = pending.NormalizedEmail,
            PasswordHash = pending.PasswordHash,
            FirstName = pending.FirstName,
            LastName = pending.LastName,
            PhoneNumber = pending.NormalizedPhoneNumber,
            NormalizedPhoneNumber = pending.NormalizedPhoneNumber,
            PhoneConfirmed = true,
            TermsAndPrivacyAcceptedAtUtc = pending.TermsAndPrivacyAcceptedAtUtc,
            TermsAndPrivacyVersion = pending.TermsAndPrivacyVersion,
            Status = UserStatus.Active,
            EmailConfirmed = true,
            RoleId = SystemRoleIds.Candidate,
            Role = new Role
            {
                Id = SystemRoleIds.Candidate,
                Name = "Candidate",
                NormalizedName = "CANDIDATE"
            }
        };
        challenge.ConsumedAtUtc = UtcNow;
        challenge.User = user;
        challenge.UserId = user.Id;
        pending.CompletedAtUtc = UtcNow;
        pending.ClosedAtUtc = UtcNow;
        pending.CompletedUser = user;
        pending.CompletedUserId = user.Id;
        pending.PasswordHash = null;
        await users.AddAsync(user, cancellationToken);
        challenges.Update(challenge);
        challenges.Update(pending);
        await auditWriter.AppendAsync(new(
            AuditAction.Create,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?> { ["source"] = "mobileOtp" },
            new(user.Id, "Candidate")), cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return new(RegistrationSuccessMessage);
        }
        return new(RegistrationSuccessMessage);
    }

    public async Task<MessageResponse> ResendRegistrationOtpAsync(
        ResendRegistrationOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        await registrationResendValidator.ValidateAndThrowAsync(request, cancellationToken);
        var challenge = await challenges.GetChallengeByIdAsync(request.ChallengeId, cancellationToken);
        var pending = challenge?.PendingRegistration;
        if (challenge is null ||
            challenge.Purpose != OtpPurpose.Registration ||
            challenge.ConsumedAtUtc is not null ||
            pending is null ||
            pending.ClosedAtUtc is not null ||
            pending.ExpiresAtUtc <= UtcNow)
            return new(RegistrationPendingMessage);

        if (UtcNow - challenge.LastSentAtUtc < ResendCooldown)
        {
            LogAuthenticationEvent(
                "send_skipped_cooldown",
                OtpPurpose.Registration,
                challenge.NormalizedPhoneNumber,
                challenge.Id,
                "skipped");
            return new(RegistrationPendingMessage);
        }

        if (await IsMobileSendRateExceededAsync(challenge.NormalizedPhoneNumber, OtpPurpose.Registration, cancellationToken))
        {
            LogAuthenticationEvent(
                "send_skipped_rate_limit",
                OtpPurpose.Registration,
                challenge.NormalizedPhoneNumber,
                challenge.Id,
                "skipped");
            return new(RegistrationPendingMessage);
        }

        var newOtp = RotateChallenge(challenge);
        pending.ExpiresAtUtc = UtcNow.Add(PendingRegistrationLifetime);
        challenges.Update(challenge);
        challenges.Update(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendPersistedOtpAsync(
            challenge.Id,
            challenge.NormalizedPhoneNumber,
            newOtp,
            OtpPurpose.Registration);

        return new(RegistrationPendingMessage);
    }

    public async Task<AuthenticationResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        request = request with { Identifier = request.Identifier?.Trim() ?? string.Empty };
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        User? user;
        if (request.Identifier.Contains('@', StringComparison.Ordinal))
        {
            user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Identifier), cancellationToken);
        }
        else
        {
            _ = IndianMobileNumber.TryNormalize(request.Identifier, out var normalizedPhoneNumber);
            user = await users.GetByNormalizedPhoneAsync(normalizedPhoneNumber, cancellationToken);
        }

        if (user is null || user.Status != UserStatus.Active || !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw InvalidCredentials();

        user.LastLoginAtUtc = UtcNow;
        users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<MessageResponse> RequestLoginOtpAsync(
        RequestLoginOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        await requestLoginOtpValidator.ValidateAndThrowAsync(request, cancellationToken);
        _ = IndianMobileNumber.TryNormalizeTenDigit(request.PhoneNumber, out var normalizedPhoneNumber);
        await RequestUserOtpAsync(normalizedPhoneNumber, OtpPurpose.Login, candidateOnly: true, cancellationToken);
        return new(OtpSentMessage);
    }

    public async Task<AuthenticationResponse> LoginWithOtpAsync(
        LoginWithOtpRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await loginWithOtpValidator.ValidateAndThrowAsync(request, cancellationToken);
        _ = IndianMobileNumber.TryNormalizeTenDigit(request.PhoneNumber, out var normalizedPhoneNumber);
        var challenge = await challenges.GetLatestForPhoneAsync(normalizedPhoneNumber, OtpPurpose.Login, cancellationToken);
        var user = challenge?.User;
        if (challenge is null ||
            user is null ||
            user.RoleId != SystemRoleIds.Candidate ||
            user.Status != UserStatus.Active ||
            !user.PhoneConfirmed)
            throw InvalidOtp();

        await ValidateOtpAsync(challenge, request.Otp, cancellationToken);
        challenge.ConsumedAtUtc = UtcNow;
        user.LastLoginAtUtc = UtcNow;
        challenges.Update(challenge);
        users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Login,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?> { ["source"] = "mobileOtp" },
            new(user.Id, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<MessageResponse> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        request = request with { Email = request.Email?.Trim() ?? string.Empty };
        await requestPasswordResetValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(
            NormalizeEmail(request.Email),
            cancellationToken);
        if (user is not { Status: UserStatus.Active })
            return new(PasswordResetRequestedMessage);

        var rawToken = GeneratePasswordResetToken();
        user.PasswordResetTokenHash = HashPasswordResetToken(rawToken);
        user.PasswordResetTokenExpiresAtUtc = UtcNow.Add(PasswordResetTokenLifetime);
        users.Update(user);
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["operation"] = "passwordResetRequested"
            },
            new(user.Id, user.Role.Name)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _ = await emailService.SendPasswordResetAsync(
            user,
            rawToken,
            cancellationToken);
        return new(PasswordResetRequestedMessage);
    }

    public async Task<MessageResponse> CompletePasswordResetAsync(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        request = request with { Email = request.Email?.Trim() ?? string.Empty };
        await completeResetValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(
            NormalizeEmail(request.Email),
            cancellationToken);
        if (user is not { Status: UserStatus.Active } ||
            string.IsNullOrWhiteSpace(user.PasswordResetTokenHash) ||
            user.PasswordResetTokenExpiresAtUtc is null ||
            user.PasswordResetTokenExpiresAtUtc <= UtcNow ||
            !VerifyPasswordResetToken(request.Token, user.PasswordResetTokenHash))
            throw InvalidPasswordReset();

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAtUtc = null;
        users.Update(user);
        await refreshTokens.RevokeActiveForUserAsync(user.Id, UtcNow, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["operation"] = "passwordResetCompleted"
            },
            new(user.Id, user.Role.Name)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(PasswordChangedMessage);
    }

    public async Task<AuthenticationResponse> RefreshAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);
        var existingToken = await refreshTokens.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (existingToken is null ||
            existingToken.RevokedAtUtc is not null ||
            existingToken.ExpiresAtUtc <= UtcNow ||
            existingToken.User.Status != UserStatus.Active)
            throw new UnauthorizedException("The refresh token is invalid or expired.");

        var response = await IssueTokensAsync(existingToken.User, ipAddress, cancellationToken);
        existingToken.RevokedAtUtc = UtcNow;
        existingToken.RevokedByIp = ipAddress;
        existingToken.ReplacedByToken = jwtTokenService.HashToken(response.RefreshToken);
        refreshTokens.Update(existingToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByIdWithRoleAsync(userId, cancellationToken) ?? throw new UnauthorizedException();
        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BadRequestException("The current password is incorrect.", "invalid_current_password");

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        users.Update(user);
        await refreshTokens.RevokeActiveForUserAsync(userId, UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAsync(
        Guid userId,
        LogoutRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(new RefreshTokenRequest(request.RefreshToken), cancellationToken);
        var token = await refreshTokens.GetByTokenHashAsync(jwtTokenService.HashToken(request.RefreshToken), cancellationToken);
        if (token is null || token.UserId != userId || token.RevokedAtUtc is not null) return;

        token.RevokedAtUtc = UtcNow;
        token.RevokedByIp = ipAddress;
        refreshTokens.Update(token);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RequestUserOtpAsync(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        bool candidateOnly,
        CancellationToken cancellationToken)
    {
        if (await IsMobileSendRateExceededAsync(normalizedPhoneNumber, purpose, cancellationToken))
        {
            LogAuthenticationEvent(
                "send_skipped_rate_limit",
                purpose,
                normalizedPhoneNumber,
                status: "skipped");
            return;
        }
        var user = await users.GetByNormalizedPhoneAsync(normalizedPhoneNumber, cancellationToken);
        var isEligible = user is { Status: UserStatus.Active, PhoneConfirmed: true } && (!candidateOnly || user.RoleId == SystemRoleIds.Candidate);

        var latest = await challenges.GetLatestForPhoneAsync(normalizedPhoneNumber, purpose, cancellationToken);
        if (latest is not null && UtcNow - latest.LastSentAtUtc < ResendCooldown)
        {
            LogAuthenticationEvent(
                "send_skipped_cooldown",
                purpose,
                normalizedPhoneNumber,
                latest.Id,
                "skipped");
            return;
        }

        string generatedOtp;
        OtpChallenge challenge;
        if (latest is not null && latest.ConsumedAtUtc is null)
        {
            challenge = latest;
            generatedOtp = RotateChallenge(challenge);
            challenge.VerifiedAtUtc = null;
            challenge.ResetChallengeExpiresAtUtc = null;
            if (isEligible)
            {
                challenge.User = user;
                challenge.UserId = user!.Id;
            }
            challenges.Update(challenge);
        }
        else
        {
            generatedOtp = otpService.Generate();
            challenge = NewChallenge(normalizedPhoneNumber, purpose, generatedOtp);
            if (isEligible)
            {
                challenge.User = user;
                challenge.UserId = user!.Id;
            }
            await challenges.AddChallengeAsync(challenge, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (isEligible)
        {
            await SendPersistedOtpAsync(
                challenge.Id,
                normalizedPhoneNumber,
                generatedOtp,
                purpose);
        }
        else
        {
            LogAuthenticationEvent(
                "send_skipped_ineligible",
                purpose,
                normalizedPhoneNumber,
                challenge.Id,
                "skipped");
        }
    }

    private async Task ValidateOtpAsync(
        OtpChallenge challenge,
        string otp,
        CancellationToken cancellationToken)
    {
        if (challenge.ConsumedAtUtc is not null ||
            challenge.ExpiresAtUtc <= UtcNow ||
            challenge.FailedAttemptCount >= MaximumOtpAttempts)
            throw InvalidOtp();
        if (otpService.Verify(otp, challenge.OtpHash)) return;

        challenge.FailedAttemptCount++;
        challenges.Update(challenge);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        throw InvalidOtp();
    }

    private async Task<bool> IsMobileSendRateExceededAsync(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var sent = await challenges.CountSentSinceAsync(
            normalizedPhoneNumber,
            purpose,
            UtcNow.Subtract(SendRateWindow),
            cancellationToken);
        return sent >= MaximumSendsPerWindow;
    }

    private async Task<SmsDeliveryResult> SendPersistedOtpAsync(
        Guid challengeId,
        string normalizedPhoneNumber,
        string otp,
        OtpPurpose purpose)
    {
        using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            applicationShutdown.ApplicationStopping);
        deliveryCancellation.CancelAfter(OtpDeliveryBudget);

        LogAuthenticationEvent(
            "sms_delivery_started_after_persist",
            purpose,
            normalizedPhoneNumber,
            challengeId,
            "started");
        try
        {
            var result = await smsService.SendOtpAsync(
                normalizedPhoneNumber,
                otp,
                purpose,
                deliveryCancellation.Token);

            var category = result switch
            {
                SmsDeliveryResult.TimedOut => "sms_delivery_timeout",
                SmsDeliveryResult.Failed => "sms_delivery_failed",
                _ => "sms_delivery_completed"
            };
            var level = result switch
            {
                SmsDeliveryResult.TimedOut => LogLevel.Warning,
                SmsDeliveryResult.Failed => LogLevel.Error,
                _ => LogLevel.Information
            };
            LogAuthenticationEvent(
                category,
                purpose,
                normalizedPhoneNumber,
                challengeId,
                result.ToString(),
                level: level);
            return result;
        }
        catch (OperationCanceledException exception)
        {
            var applicationStopping =
                applicationShutdown.ApplicationStopping.IsCancellationRequested;
            LogAuthenticationEvent(
                applicationStopping
                    ? "sms_delivery_failed"
                    : "sms_delivery_timeout",
                purpose,
                normalizedPhoneNumber,
                challengeId,
                applicationStopping ? "application_shutdown" : "timeout",
                exception,
                applicationStopping ? LogLevel.Error : LogLevel.Warning);
            throw;
        }
        catch (Exception exception)
        {
            LogAuthenticationEvent(
                "sms_delivery_failed",
                purpose,
                normalizedPhoneNumber,
                challengeId,
                "failed",
                exception,
                LogLevel.Error);
            throw;
        }
    }

    private void LogAuthenticationEvent(
        string category,
        OtpPurpose purpose,
        string normalizedPhoneNumber,
        Guid? challengeId = null,
        string status = "none",
        Exception? exception = null,
        LogLevel level = LogLevel.Information)
    {
        var write = level switch
        {
            LogLevel.Error => AuthenticationError,
            LogLevel.Warning => AuthenticationWarning,
            _ => AuthenticationInformation
        };
        write(
            logger,
            category,
            purpose,
            challengeId?.ToString() ?? "none",
            SafePhoneSuffix(normalizedPhoneNumber),
            status,
            exception?.GetType().Name ?? "none",
            null);
    }

    private static string SafePhoneSuffix(string? normalizedPhoneNumber)
    {
        if (string.IsNullOrWhiteSpace(normalizedPhoneNumber))
            return "unavailable";
        var digits = new string(normalizedPhoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : "unavailable";
    }

    private OtpChallenge NewChallenge(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        string otp) => new()
        {
            Purpose = purpose,
            NormalizedPhoneNumber = normalizedPhoneNumber,
            OtpHash = otpService.Hash(otp),
            ExpiresAtUtc = UtcNow.Add(OtpLifetime),
            LastSentAtUtc = UtcNow,
            SendCount = 1
        };

    private string RotateChallenge(OtpChallenge challenge)
    {
        var otp = otpService.Generate();
        challenge.OtpHash = otpService.Hash(otp);
        challenge.ExpiresAtUtc = UtcNow.Add(OtpLifetime);
        challenge.FailedAttemptCount = 0;
        challenge.LastSentAtUtc = UtcNow;
        challenge.SendCount++;
        return otp;
    }

    private async Task<AuthenticationResponse> IssueTokensAsync(
        User user,
        string? ipAddress,
        CancellationToken cancellationToken)
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
        return new(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshToken.ExpiresAtUtc,
            new(user.Id, user.Email, user.FirstName, user.LastName, user.Role.Name));
    }

    private RegistrationChallengeResponse DecoyRegistrationResponse() => new(
        Guid.NewGuid(),
        RegistrationPendingMessage,
        UtcNow.Add(OtpLifetime));

    private static UnauthorizedException InvalidCredentials() => new("Invalid identifier or password.");

    private static BadRequestException InvalidOtp() => new("The OTP is invalid or expired.", "invalid_otp");

    private static BadRequestException InvalidPasswordReset() => new(
        "The password reset link is invalid or expired.",
        "invalid_password_reset");

    private static string GeneratePasswordResetToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashPasswordResetToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static bool VerifyPasswordResetToken(
        string rawToken,
        string expectedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(HashPasswordResetToken(rawToken)),
                Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
