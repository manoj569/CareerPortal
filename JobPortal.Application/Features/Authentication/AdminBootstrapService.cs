using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Authentication;

public sealed record AdminBootstrapSettings(
    bool Enabled, string? Email, string? Password, string? FirstName, string? LastName);

public enum AdminBootstrapResult { Disabled, AlreadyExists, Created }

public sealed class AdminBootstrapService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IValidator<RegisterRequest> registerValidator)
{
    public async Task<AdminBootstrapResult> InitializeAsync(
        AdminBootstrapSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled) return AdminBootstrapResult.Disabled;

        var email = Required(settings.Email, "Email");
        var password = Required(settings.Password, "Password");
        var firstName = Required(settings.FirstName, "FirstName");
        var lastName = Required(settings.LastName, "LastName");
        RejectPlaceholder(email, "Email");
        RejectPlaceholder(password, "Password");

        try
        {
            await registerValidator.ValidateAndThrowAsync(
                new RegisterRequest(
                    $"{firstName} {lastName}",
                    email,
                    password,
                    "9876543210",
                    true),
                cancellationToken);
        }
        catch (ValidationException exception)
        {
            throw new InvalidOperationException(
                "BootstrapAdmin configuration does not satisfy the account validation rules.", exception);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await users.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            if (existing.RoleId != SystemRoleIds.Administrator)
                throw new InvalidOperationException(
                    "BootstrapAdmin email belongs to a non-Administrator account. Automatic role elevation is forbidden.");
            return AdminBootstrapResult.AlreadyExists;
        }

        await users.AddAsync(new User
        {
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = passwordHasher.Hash(password),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Status = UserStatus.Active,
            EmailConfirmed = true,
            RoleId = SystemRoleIds.Administrator,
            Role = new Role
            {
                Id = SystemRoleIds.Administrator,
                Name = "Administrator",
                NormalizedName = "ADMINISTRATOR"
            }
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AdminBootstrapResult.Created;
    }

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"BootstrapAdmin:{name} is required when bootstrap is enabled.");

    private static void RejectPlaceholder(string value, string name)
    {
        string[] markers = ["configure", "change_me", "changeme", "placeholder", "your_", "example"];
        if (markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"BootstrapAdmin:{name} contains an obvious placeholder value.");
    }
}
