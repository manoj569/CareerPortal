using FluentValidation;

namespace JobPortal.Application.Features.Dashboard;

public sealed class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.ProfileImageUrl).MaximumLength(2048)
            .Must(BeOptionalHttpUrl).WithMessage("ProfileImageUrl must be an absolute HTTP or HTTPS URL.");
        RuleFor(x => x.Headline).MaximumLength(250);
        RuleFor(x => x.Bio).MaximumLength(4000);
    }

    private static bool BeOptionalHttpUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}
