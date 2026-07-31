using System.Linq.Expressions;
using FluentValidation;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Jobs;

internal static class JobValidationRules
{
    public static void Apply<T>(AbstractValidator<T> validator,
        Expression<Func<T, string>> title, Expression<Func<T, string>> description,
        Expression<Func<T, Guid>> companyId, Expression<Func<T, Guid>> categoryId,
        Expression<Func<T, string?>> responsibilities, Expression<Func<T, string?>> requirements,
        Expression<Func<T, string?>> benefits, Expression<Func<T, string?>> location,
        Expression<Func<T, decimal?>> minimumSalary, Expression<Func<T, decimal?>> maximumSalary,
        Expression<Func<T, string>> currencyCode,
        Expression<Func<T, EmploymentType>> employmentType,
        Expression<Func<T, WorkplaceType>> workplaceType,
        Expression<Func<T, ExperienceLevel>> experienceLevel)
    {
        validator.RuleFor(title).NotEmpty().MaximumLength(250);
        validator.RuleFor(description).NotEmpty().MaximumLength(16000);
        validator.RuleFor(companyId).NotEmpty();
        validator.RuleFor(categoryId).NotEmpty();
        validator.RuleFor(responsibilities).MaximumLength(8000);
        validator.RuleFor(requirements).MaximumLength(8000);
        validator.RuleFor(benefits).MaximumLength(4000);
        validator.RuleFor(location).MaximumLength(250);
        validator.RuleFor(minimumSalary).GreaterThanOrEqualTo(0);
        validator.RuleFor(maximumSalary).GreaterThanOrEqualTo(0);
        validator.RuleFor(currencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        validator.RuleFor(employmentType).IsInEnum();
        validator.RuleFor(workplaceType).IsInEnum();
        validator.RuleFor(experienceLevel).IsInEnum();
        var minimumSalaryAccessor = minimumSalary.Compile();
        var maximumSalaryAccessor = maximumSalary.Compile();
        validator.RuleFor(maximumSalary)
            .GreaterThanOrEqualTo(minimumSalary)
            .When(x => minimumSalaryAccessor(x).HasValue && maximumSalaryAccessor(x).HasValue)
            .WithMessage("Maximum salary must be greater than or equal to minimum salary.");
    }
}

public sealed class CreateJobRequestValidator : AbstractValidator<CreateJobRequest>
{
    public CreateJobRequestValidator()
    {
        JobValidationRules.Apply(this, x => x.Title, x => x.Description, x => x.CompanyId, x => x.CategoryId,
            x => x.Responsibilities, x => x.Requirements, x => x.Benefits, x => x.Location,
            x => x.MinimumSalary, x => x.MaximumSalary, x => x.CurrencyCode,
            x => x.EmploymentType, x => x.WorkplaceType, x => x.ExperienceLevel);
        RuleFor(x => x.ApplicationUrl).NotEmpty().MaximumLength(2048)
            .Must(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithMessage("ApplicationUrl must be an absolute HTTP or HTTPS URL.");
    }
}

public sealed class UpdateJobRequestValidator : AbstractValidator<UpdateJobRequest>
{
    public UpdateJobRequestValidator()
    {
        JobValidationRules.Apply(this, x => x.Title, x => x.Description, x => x.CompanyId, x => x.CategoryId,
            x => x.Responsibilities, x => x.Requirements, x => x.Benefits, x => x.Location,
            x => x.MinimumSalary, x => x.MaximumSalary, x => x.CurrencyCode,
            x => x.EmploymentType, x => x.WorkplaceType, x => x.ExperienceLevel);
        RuleFor(x => x.ApplicationUrl).NotEmpty().MaximumLength(2048)
            .Must(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithMessage("ApplicationUrl must be an absolute HTTP or HTTPS URL.");
    }
}

public sealed class JobSearchQueryValidator : AbstractValidator<JobSearchQuery>
{
    private static readonly string[] SortFields =
        ["createdAt", "updatedAt", "publishedAt", "title", "minimumSalary", "maximumSalary", "expiresAt", "status"];

    public JobSearchQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortFields)}.");
        RuleFor(x => x.SortDirection).Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) || x.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");
        RuleFor(x => x.PublishedToUtc).GreaterThanOrEqualTo(x => x.PublishedFromUtc)
            .When(x => x.PublishedFromUtc.HasValue && x.PublishedToUtc.HasValue);
        RuleFor(x => x.ExpiresToUtc).GreaterThanOrEqualTo(x => x.ExpiresFromUtc)
            .When(x => x.ExpiresFromUtc.HasValue && x.ExpiresToUtc.HasValue);
    }

    public sealed class UpdateRecruiterContactRequestValidator
    : AbstractValidator<UpdateRecruiterContactRequest>
    {
        public UpdateRecruiterContactRequestValidator()
        {
            RuleFor(x => x.ContactName)
                .NotEmpty()
                .MaximumLength(150);

            RuleFor(x => x.ContactRole)
                .NotEmpty()
                .MaximumLength(150);

            RuleFor(x => x.Email)
                .NotEmpty()
                .MaximumLength(256)
                .EmailAddress();

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(32)
                .Matches(@"^\+?[0-9 ()-]{7,32}$")
                .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
                .WithMessage("Phone number must contain only valid phone characters.");
        }
    }
}
