using FluentValidation;

namespace JobPortal.Application.Features.Candidates;

public sealed class UpdateCandidateProfileRequestValidator : AbstractValidator<UpdateCandidateProfileRequest>
{
    public UpdateCandidateProfileRequestValidator()
    {
        RuleFor(x => x.Headline).MaximumLength(250);
        RuleFor(x => x.Bio).MaximumLength(4000);
        RuleFor(x => x.Location).MaximumLength(250);
        RuleFor(x => x.LinkedInUrl).MaximumLength(2048).Must(OptionalUrl);
        RuleFor(x => x.PortfolioUrl).MaximumLength(2048).Must(OptionalUrl);
        RuleFor(x => x.Skills).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 50);
        RuleForEach(x => x.Skills).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Education).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 20);
        RuleForEach(x => x.Education).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Experience).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 30);
        RuleForEach(x => x.Experience).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.PreferredJobTypes).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 10);
        RuleForEach(x => x.PreferredJobTypes).IsInEnum();
    }

    private static bool OptionalUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}

public sealed class CandidatePageQueryValidator : AbstractValidator<CandidatePageQuery>
{
    public CandidatePageQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class JobApplicationQueryValidator : AbstractValidator<JobApplicationQuery>
{
    public JobApplicationQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

public sealed class CreateJobApplicationRequestValidator : AbstractValidator<CreateJobApplicationRequest>
{
    public CreateJobApplicationRequestValidator() =>
        RuleFor(x => x.CoverLetter).MaximumLength(5000);
}
