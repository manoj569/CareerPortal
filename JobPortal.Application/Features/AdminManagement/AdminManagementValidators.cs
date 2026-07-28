using FluentValidation;

namespace JobPortal.Application.Features.AdminManagement;

internal static class AdminValidationRules
{
    public static IRuleBuilderOptions<T, string?> OptionalHttpUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(2048)
            .Must(value => string.IsNullOrWhiteSpace(value) ||
                (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            .WithMessage("The value must be an absolute HTTP or HTTPS URL.");
}

public sealed class CreateCompanyRequestValidator : AbstractValidator<CreateCompanyRequest>
{
    public CreateCompanyRequestValidator() => ApplyRules();
    private void ApplyRules()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).MaximumLength(220);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.WebsiteUrl).OptionalHttpUrl();
        RuleFor(x => x.LogoUrl).OptionalHttpUrl();
        RuleFor(x => x.Industry).MaximumLength(150);
        RuleFor(x => x.Location).MaximumLength(250);
        RuleFor(x => x.EmployeeCount).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCompanyRequestValidator : AbstractValidator<UpdateCompanyRequest>
{
    public UpdateCompanyRequestValidator()
    {
        Include(new CompanyUpdateRules());
    }

    private sealed class CompanyUpdateRules : AbstractValidator<UpdateCompanyRequest>
    {
        public CompanyUpdateRules()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Slug).MaximumLength(220);
            RuleFor(x => x.Description).MaximumLength(4000);
            RuleFor(x => x.WebsiteUrl).OptionalHttpUrl();
            RuleFor(x => x.LogoUrl).OptionalHttpUrl();
            RuleFor(x => x.Industry).MaximumLength(150);
            RuleFor(x => x.Location).MaximumLength(250);
            RuleFor(x => x.EmployeeCount).GreaterThanOrEqualTo(0);
        }
    }
}

public sealed class CompanySearchQueryValidator : AbstractValidator<CompanySearchQuery>
{
    private static readonly string[] SortFields = ["createdAt", "updatedAt", "name", "industry", "employeeCount"];
    public CompanySearchQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.SortDirection).Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Slug).MaximumLength(170);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Slug).MaximumLength(170);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CategorySearchQueryValidator : AbstractValidator<CategorySearchQuery>
{
    private static readonly string[] SortFields = ["createdAt", "updatedAt", "name", "displayOrder"];
    public CategorySearchQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.SortDirection).Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}
