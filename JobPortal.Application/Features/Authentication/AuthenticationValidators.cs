using FluentValidation;
using JobPortal.Application.Common.Text;

namespace JobPortal.Application.Features.Authentication;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MinimumLength(2).MaximumLength(100)
            .Must(BeSafeName).WithMessage("FirstName contains invalid characters.");
        //RuleFor(x => x.LastName).NotEmpty().MinimumLength(2).MaximumLength(100)
        //    .Must(BeSafeName).WithMessage("LastName contains invalid characters.");
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32)
            .Must(value => IndianMobileNumber.TryNormalize(value, out _))
            .WithMessage("PhoneNumber must be a valid Indian mobile number.");
        RuleFor(x => x.HasAcceptedTermsAndPrivacy).Equal(true)
            .WithMessage("Terms and Privacy consent is required.");
        //RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128)
        //    .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
        //    .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
        //    .Matches("[0-9]").WithMessage("Password must contain a number.")
        //    .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character.")
        //    .Must((request, password) => DoesNotContainPersonalData(request, password))
        //    .WithMessage("Password must not contain your name, email name, or mobile number.");
    }

    private static bool BeSafeName(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0 &&
            !trimmed.Any(char.IsControl) &&
            trimmed.Any(char.IsLetter);
    }

    private static bool DoesNotContainPersonalData(
        RegisterRequest request, string password)
    {
        if (string.IsNullOrEmpty(password))
            return true;
        var emailLocalPart = request.Email.Trim().Split('@', 2)[0];
        _ = IndianMobileNumber.TryNormalize(request.PhoneNumber, out var mobile);
        var values = new[]
        {
            emailLocalPart,
            request.FirstName.Trim(),
            //request.LastName.Trim(),
            mobile,
            mobile.TrimStart('+'),
            mobile.Length == 13 ? mobile[3..] : string.Empty
        };
        return values
            .Where(value => value.Length > 0)
            .All(value => !password.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator() { RuleFor(x => x.Email).NotEmpty().EmailAddress(); RuleFor(x => x.Password).NotEmpty().MaximumLength(128); }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() { RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512); }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() { RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256); }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.NewPassword).SetValidator(new PasswordValidator());
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).SetValidator(new PasswordValidator());
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword).WithMessage("New password must differ from the current password.");
    }
}

internal sealed class PasswordValidator : AbstractValidator<string>
{
    public PasswordValidator()
    {
        RuleFor(x => x).NotEmpty().MinimumLength(12).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character.");
    }
}
