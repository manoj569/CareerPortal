using FluentValidation;

namespace JobPortal.Application.Features.PublicJobs;

public sealed class PublicJobQueryValidator : AbstractValidator<PublicJobQuery>
{
    private static readonly string[] SortFields =
        ["publishedAt", "createdAt", "title", "minimumSalary", "maximumSalary", "expiresAt"];

    public PublicJobQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
        RuleFor(x => x.Location).MaximumLength(250);
        RuleFor(x => x.MinimumSalary).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaximumSalary).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaximumSalary).GreaterThanOrEqualTo(x => x.MinimumSalary)
            .When(x => x.MinimumSalary.HasValue && x.MaximumSalary.HasValue);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortFields)}.");
        RuleFor(x => x.SortDirection)
            .Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
                       x.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");
    }
}
