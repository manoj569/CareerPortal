using System.Globalization;
using System.Text;
using FluentValidation;
using JobPortal.Application.Abstractions.AdminImports;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.AdminImports;

public sealed class AdminImportService(
    IAdminImportRepository imports,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreateCompanyRequest> companyValidator,
    IValidator<CreateJobRequest> jobValidator,
    TimeProvider timeProvider) : IAdminImportService
{
    private static readonly string[] CompanyRequiredHeaders =
    [
        "name", "websiteUrl", "industry", "location", "employeeCount",
        "description", "isVerified"
    ];

    private static readonly string[] CompanyOptionalHeaders = ["companyType"];

    private static readonly string[] JobRequiredHeaders =
    [
        "title", "companyName", "categoryName", "description",
        "applicationUrl", "employmentType", "workplaceType",
        "experienceLevel", "location", "minSalary", "maxSalary",
        "currencyCode", "expiresAtUtc", "responsibilities", "requirements",
        "benefits", "isFeatured"
    ];

    private static readonly string[] JobOptionalHeaders =
    [
        "minExperienceYears", "maxExperienceYears", "internshipDurationMonths",
        "isFlexibleDuration", "department", "roleCategory", "educationRequirement",
        "postedByType"
    ];

    public async Task<CsvImportResult> PreviewCompaniesAsync(
        CsvImportFile file,
        CancellationToken cancellationToken = default) =>
        (await EvaluateCompaniesAsync(file, cancellationToken)).ToResult();

    public async Task<CsvImportResult> CommitCompaniesAsync(
        Guid administratorUserId,
        CsvImportFile file,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateCompaniesAsync(file, cancellationToken);
        if (evaluation.InvalidRows > 0)
            return evaluation.ToResult();

        var additions = new List<Company>();
        foreach (var action in evaluation.Actions)
        {
            if (action.Existing is null)
            {
                additions.Add(new Company
                {
                    OwnerUserId = administratorUserId,
                    Name = action.Values.Name,
                    Slug = action.Values.Slug,
                    WebsiteUrl = action.Values.WebsiteUrl,
                    Industry = action.Values.Industry,
                    Location = action.Values.Location,
                    EmployeeCount = action.Values.EmployeeCount,
                    Description = action.Values.Description,
                    CompanyType = action.Values.CompanyType,
                    IsVerified = false
                });
                continue;
            }

            ApplySafeCompanyUpdate(action.Existing, action.Values);
            imports.UpdateCompany(action.Existing);
        }

        await imports.AddCompaniesAsync(additions, cancellationToken);
        await AppendAuditAsync(
            "companies",
            evaluation,
            evaluation.Actions.Count,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return evaluation.ToCommittedResult(
            action => action.Existing is null ? "Imported" : "Updated");
    }

    public async Task<CsvImportResult> PreviewJobsAsync(
        CsvImportFile file,
        CancellationToken cancellationToken = default) =>
        (await EvaluateJobsAsync(file, cancellationToken)).ToResult();

    public async Task<CsvImportResult> CommitJobsAsync(
        CsvImportFile file,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateJobsAsync(file, cancellationToken);
        if (evaluation.InvalidRows > 0)
            return evaluation.ToResult();

        var jobs = evaluation.Actions.Select(action => NewDraftJob(action.Request)).ToArray();
        await imports.AddJobsAsync(jobs, cancellationToken);
        await AppendAuditAsync(
            "jobs",
            evaluation,
            jobs.Length,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return evaluation.ToCommittedResult(_ => "Imported");
    }

    public CsvImportTemplate GetCompaniesTemplate() => Template(
        "companies-template.csv",
        "name,websiteUrl,industry,location,employeeCount,description,isVerified,companyType\r\n" +
        "Example Learning Labs,https://example.invalid,Education,\"Pune, Maharashtra\",120,\"Fictional company for import testing\",false,Startup\r\n");

    public CsvImportTemplate GetJobsTemplate() => Template(
        "jobs-template.csv",
        "title,companyName,categoryName,description,applicationUrl,employmentType,workplaceType,experienceLevel,location,minSalary,maxSalary,currencyCode,expiresAtUtc,responsibilities,requirements,benefits,isFeatured,minExperienceYears,maxExperienceYears,internshipDurationMonths,isFlexibleDuration,department,roleCategory,educationRequirement,postedByType\r\n" +
        "Example Software Intern,Example Learning Labs,Technology,\"Fictional role for template testing\",https://jobs.example.invalid/apply/example-role,Internship,Hybrid,Entry,Pune,,,INR,,\"Assist with sample projects\",\"Basic programming knowledge\",\"Learning allowance\",false,0,1,3,false,Engineering,Software Development,B.Tech/B.E.,Company\r\n");

    private async Task<CompanyEvaluation> EvaluateCompaniesAsync(
        CsvImportFile file,
        CancellationToken cancellationToken)
    {
        var rows = await SecureCsvParser.ParseAsync(
            file,
            CompanyRequiredHeaders,
            CompanyOptionalHeaders,
            cancellationToken);
        var names = rows.Select(row => Value(row, "name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var slugs = rows.Select(row => SlugGenerator.Generate(Value(row, "name")))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existingCompanies = await imports.FindCompaniesAsync(
            slugs,
            names,
            cancellationToken);

        var actions = new List<CompanyImportAction>();
        var results = new List<CsvImportRowResult>(rows.Count);
        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var row in rows)
        {
            var errors = new List<CsvImportFieldError>();
            var values = ParseCompanyValues(row, errors);
            var validation = await companyValidator.ValidateAsync(
                new CreateCompanyRequest(
                    values.Name,
                    values.Slug,
                    values.Description,
                    values.WebsiteUrl,
                    null,
                    values.Industry,
                    values.Location,
                    values.EmployeeCount,
                    false,
                    values.CompanyType),
                cancellationToken);
            AddValidationErrors(errors, validation.Errors, CompanyFieldName);
            if (values.Slug.Length == 0)
                AddError(errors, "name", "A valid company slug could not be generated.");

            var matches = FindCompanyMatches(existingCompanies, values.Name, values.Slug);
            if (matches.Length > 1)
                AddError(errors, "name", "The company name matches multiple existing companies.");
            var existing = matches.Length == 1 ? matches[0] : null;

            if (errors.Count > 0)
            {
                results.Add(InvalidRow(row.RowNumber, errors));
                continue;
            }

            if (!seenSlugs.Add(values.Slug))
            {
                duplicates++;
                results.Add(Row(row.RowNumber, "Skip duplicate"));
                continue;
            }

            actions.Add(new(row.RowNumber, values, existing));
            results.Add(Row(
                row.RowNumber,
                existing is null ? "Valid" : "Update existing"));
        }

        return new(rows.Count, actions, results, duplicates);
    }

    private async Task<JobEvaluation> EvaluateJobsAsync(
        CsvImportFile file,
        CancellationToken cancellationToken)
    {
        var rows = await SecureCsvParser.ParseAsync(
            file,
            JobRequiredHeaders,
            JobOptionalHeaders,
            cancellationToken);
        var companyNames = rows.Select(row => Value(row, "companyName"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var companySlugs = rows.Select(row => SlugGenerator.Generate(Value(row, "companyName")))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var categoryNames = rows.Select(row => Value(row, "categoryName"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var companies = await imports.FindCompaniesAsync(
            companySlugs,
            companyNames,
            cancellationToken);
        var categories = await imports.FindCategoriesAsync(
            categoryNames,
            cancellationToken);
        var existingJobs = await imports.FindJobIdentitiesAsync(
            companies.Select(company => company.Id).Distinct().ToArray(),
            cancellationToken);
        var duplicateKeys = existingJobs.Select(JobKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var actions = new List<JobImportAction>();
        var results = new List<CsvImportRowResult>(rows.Count);
        var duplicates = 0;
        foreach (var row in rows)
        {
            var errors = new List<CsvImportFieldError>();
            var company = ResolveCompany(
                companies,
                Value(row, "companyName"),
                errors);
            var category = ResolveCategory(
                categories,
                Value(row, "categoryName"),
                errors);
            var request = ParseJobRequest(row, company, category, errors);
            var validation = await jobValidator.ValidateAsync(
                request,
                cancellationToken);
            AddValidationErrors(errors, validation.Errors, JobFieldName);
            if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc <= UtcNow)
                AddError(errors, "expiresAtUtc", "ExpiresAtUtc must be in the future.");

            if (errors.Count > 0)
            {
                results.Add(InvalidRow(row.RowNumber, errors));
                continue;
            }

            var key = JobKey(request.CompanyId, request.Title, request.ApplicationUrl);
            if (!duplicateKeys.Add(key))
            {
                duplicates++;
                results.Add(Row(row.RowNumber, "Skip duplicate"));
                continue;
            }

            actions.Add(new(row.RowNumber, request));
            results.Add(Row(row.RowNumber, "Valid"));
        }

        return new(rows.Count, actions, results, duplicates);
    }

    private static CompanyImportValues ParseCompanyValues(
        ParsedCsvRow row,
        ICollection<CsvImportFieldError> errors)
    {
        var employeeCountText = Value(row, "employeeCount");
        int? employeeCount = null;
        if (employeeCountText.Length > 0)
        {
            if (int.TryParse(
                    employeeCountText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedEmployeeCount))
                employeeCount = parsedEmployeeCount;
            else
                AddError(errors, "employeeCount", "EmployeeCount must be a whole number.");
        }

        var isVerifiedText = Value(row, "isVerified");
        if (isVerifiedText.Length > 0 && !bool.TryParse(isVerifiedText, out _))
            AddError(errors, "isVerified", "IsVerified must be true or false.");
        var companyType = ParseOptionalEnum<CompanyType>(row, "companyType", errors);

        var name = Value(row, "name").Trim();
        return new(
            name,
            SlugGenerator.Generate(name),
            Optional(row, "websiteUrl"),
            Optional(row, "industry"),
            Optional(row, "location"),
            employeeCount,
            Optional(row, "description"),
            companyType,
            Value(row, "websiteUrl").Length > 0,
            Value(row, "industry").Length > 0,
            Value(row, "location").Length > 0,
            employeeCountText.Length > 0,
            Value(row, "description").Length > 0,
            Value(row, "companyType").Length > 0);
    }

    private static CreateJobRequest ParseJobRequest(
        ParsedCsvRow row,
        Company? company,
        Category? category,
        ICollection<CsvImportFieldError> errors)
    {
        var minimumSalary = ParseDecimal(row, "minSalary", errors);
        var maximumSalary = ParseDecimal(row, "maxSalary", errors);
        var expiresAtUtc = ParseDateTime(row, "expiresAtUtc", errors);
        var employmentType = ParseEnum<EmploymentType>(row, "employmentType", errors);
        var workplaceType = ParseEnum<WorkplaceType>(row, "workplaceType", errors);
        var experienceLevel = ParseEnum<ExperienceLevel>(row, "experienceLevel", errors);
        var minimumExperienceYears = ParseInt(row, "minExperienceYears", errors);
        var maximumExperienceYears = ParseInt(row, "maxExperienceYears", errors);
        var internshipDurationMonths = ParseInt(
            row,
            "internshipDurationMonths",
            errors);
        var isFlexibleDuration = ParseOptionalBool(
            row,
            "isFlexibleDuration",
            errors);
        var postedByType = ParseOptionalEnum<PostedByType>(
            row,
            "postedByType",
            errors);
        var isFeatured = Value(row, "isFeatured");
        if (isFeatured.Length > 0 && !bool.TryParse(isFeatured, out _))
            AddError(errors, "isFeatured", "IsFeatured must be true or false.");

        return new(
            Value(row, "title"),
            Value(row, "description"),
            company?.Id ?? Guid.Empty,
            category?.Id ?? Guid.Empty,
            Value(row, "applicationUrl"),
            Optional(row, "responsibilities"),
            Optional(row, "requirements"),
            Optional(row, "benefits"),
            Optional(row, "location"),
            minimumSalary,
            maximumSalary,
            Value(row, "currencyCode"),
            employmentType,
            workplaceType,
            experienceLevel,
            expiresAtUtc,
            minimumExperienceYears,
            maximumExperienceYears,
            internshipDurationMonths,
            isFlexibleDuration,
            Optional(row, "department"),
            Optional(row, "roleCategory"),
            Optional(row, "educationRequirement"),
            postedByType);
    }

    private static Company? ResolveCompany(
        IReadOnlyCollection<Company> companies,
        string requestedName,
        ICollection<CsvImportFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            AddError(errors, "companyName", "CompanyName is required.");
            return null;
        }

        var matches = FindCompanyMatches(
            companies,
            requestedName.Trim(),
            SlugGenerator.Generate(requestedName));
        if (matches.Length == 0)
            AddError(errors, "companyName", "The referenced company does not exist.");
        else if (matches.Length > 1)
            AddError(errors, "companyName", "The company name is ambiguous.");
        return matches.Length == 1 ? matches[0] : null;
    }

    private static Category? ResolveCategory(
        IReadOnlyCollection<Category> categories,
        string requestedName,
        ICollection<CsvImportFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            AddError(errors, "categoryName", "CategoryName is required.");
            return null;
        }

        var matches = categories.Where(category =>
                string.Equals(
                    NormalizeName(category.Name),
                    NormalizeName(requestedName),
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
            AddError(errors, "categoryName", "The referenced category does not exist.");
        else if (matches.Length > 1)
            AddError(errors, "categoryName", "The category name is ambiguous.");
        return matches.Length == 1 ? matches[0] : null;
    }

    private static Company[] FindCompanyMatches(
        IReadOnlyCollection<Company> companies,
        string name,
        string slug) =>
        companies.Where(company =>
                string.Equals(company.Slug, slug, StringComparison.Ordinal) ||
                string.Equals(
                    NormalizeName(company.Name),
                    NormalizeName(name),
                    StringComparison.Ordinal))
            .DistinctBy(company => company.Id)
            .ToArray();

    private static decimal? ParseDecimal(
        ParsedCsvRow row,
        string field,
        ICollection<CsvImportFieldError> errors)
    {
        var value = Value(row, field);
        if (value.Length == 0)
            return null;
        if (decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed))
            return parsed;
        AddError(errors, field, $"{field} must be a valid decimal number.");
        return null;
    }

    private static int? ParseInt(
        ParsedCsvRow row,
        string field,
        ICollection<CsvImportFieldError> errors)
    {
        var value = Value(row, field);
        if (value.Length == 0)
            return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        AddError(errors, field, $"{field} must be a whole number.");
        return null;
    }

    private static bool ParseOptionalBool(
        ParsedCsvRow row,
        string field,
        ICollection<CsvImportFieldError> errors)
    {
        var value = Value(row, field);
        if (value.Length == 0)
            return false;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        AddError(errors, field, $"{field} must be true or false.");
        return false;
    }

    private static DateTime? ParseDateTime(
        ParsedCsvRow row,
        string field,
        ICollection<CsvImportFieldError> errors)
    {
        var value = Value(row, field);
        if (value.Length == 0)
            return null;
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return parsed.UtcDateTime;
        AddError(errors, field, $"{field} must be a valid UTC date and time.");
        return null;
    }

    private static T ParseEnum<T>(
        ParsedCsvRow row,
        string field,
        ICollection<CsvImportFieldError> errors)
        where T : struct, Enum
    {
        var value = Value(row, field);
        if (value.Length == 0)
        {
            AddError(errors, field, $"{field} is required.");
            return default;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            Enum.TryParse<T>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
            return parsed;
        AddError(
            errors,
            field,
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
        return default;
    }

    private static T? ParseOptionalEnum<T>(
        ParsedCsvRow row,
        string field,
        ICollection<CsvImportFieldError> errors)
        where T : struct, Enum
    {
        var value = Value(row, field);
        if (value.Length == 0)
            return null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            Enum.TryParse<T>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
            return parsed;
        AddError(
            errors,
            field,
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
        return null;
    }

    private static void ApplySafeCompanyUpdate(
        Company company,
        CompanyImportValues values)
    {
        company.Name = values.Name;
        company.Slug = values.Slug;
        if (values.WebsiteUrlSupplied)
            company.WebsiteUrl = values.WebsiteUrl;
        if (values.IndustrySupplied)
            company.Industry = values.Industry;
        if (values.LocationSupplied)
            company.Location = values.Location;
        if (values.EmployeeCountSupplied)
            company.EmployeeCount = values.EmployeeCount;
        if (values.DescriptionSupplied)
            company.Description = values.Description;
        if (values.CompanyTypeSupplied)
            company.CompanyType = values.CompanyType;
    }

    private Job NewDraftJob(CreateJobRequest request)
    {
        var id = Guid.NewGuid();
        var job = new Job
        {
            Id = id,
            ReferenceNumber = $"JOB-{UtcNow:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            Slug = $"{SlugGenerator.Generate(request.Title, 240)}-{id.ToString("N")[..8]}",
            Status = JobStatus.Draft,
            IsHidden = false,
            IsFeatured = false,
            PublishedAtUtc = null
        };
        job.Apply(new UpdateJobRequest(
            request.Title,
            request.Description,
            request.CompanyId,
            request.CategoryId,
            request.ApplicationUrl,
            request.Responsibilities,
            request.Requirements,
            request.Benefits,
            request.Location,
            request.MinimumSalary,
            request.MaximumSalary,
            request.CurrencyCode,
            request.EmploymentType,
            request.WorkplaceType,
            request.ExperienceLevel,
            request.ExpiresAtUtc,
            request.MinimumExperienceYears,
            request.MaximumExperienceYears,
            request.InternshipDurationMonths,
            request.IsFlexibleDuration,
            request.Department,
            request.RoleCategory,
            request.EducationRequirement,
            request.PostedByType));
        return job;
    }

    private async Task AppendAuditAsync<TAction>(
        string importType,
        ImportEvaluation<TAction> evaluation,
        int importedRows,
        CancellationToken cancellationToken)
    {
        await auditWriter.AppendAsync(new(
            AuditAction.Upload,
            "AdminCsvImport",
            Guid.NewGuid().ToString(),
            new Dictionary<string, string?>
            {
                ["importType"] = importType,
                ["totalRows"] = evaluation.TotalRows.ToString(CultureInfo.InvariantCulture),
                ["validRows"] = evaluation.Actions.Count.ToString(CultureInfo.InvariantCulture),
                ["invalidRows"] = evaluation.InvalidRows.ToString(CultureInfo.InvariantCulture),
                ["duplicateRows"] = evaluation.DuplicateRows.ToString(CultureInfo.InvariantCulture),
                ["importedRows"] = importedRows.ToString(CultureInfo.InvariantCulture),
                ["skippedRows"] = evaluation.DuplicateRows.ToString(CultureInfo.InvariantCulture)
            }),
            cancellationToken);
    }

    private static void AddValidationErrors(
        ICollection<CsvImportFieldError> errors,
        IEnumerable<FluentValidation.Results.ValidationFailure> failures,
        Func<string, string> fieldName)
    {
        foreach (var failure in failures)
            AddError(errors, fieldName(failure.PropertyName), failure.ErrorMessage);
    }

    private static void AddError(
        ICollection<CsvImportFieldError> errors,
        string field,
        string message)
    {
        if (!errors.Any(error =>
                string.Equals(error.Field, field, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(error.Message, message, StringComparison.Ordinal)))
            errors.Add(new(field, message));
    }

    private static CsvImportRowResult InvalidRow(
        int rowNumber,
        IReadOnlyCollection<CsvImportFieldError> errors) =>
        new(rowNumber, "Invalid", errors);

    private static CsvImportRowResult Row(int rowNumber, string status) =>
        new(rowNumber, status, Array.Empty<CsvImportFieldError>());

    private static string Value(ParsedCsvRow row, string name) =>
        row.Fields[name];

    private static string? Optional(ParsedCsvRow row, string name) =>
        TextNormalizer.TrimOrNull(Value(row, name));

    private static string NormalizeName(string value) =>
        value.Trim().ToUpperInvariant();

    private static string JobKey(ExistingJobImportIdentity identity) =>
        JobKey(identity.CompanyId, identity.Title, identity.ApplicationUrl);

    private static string JobKey(
        Guid companyId,
        string title,
        string applicationUrl) =>
        $"{companyId:N}\u001F{title.Trim()}\u001F{applicationUrl.Trim()}";

    private static string CompanyFieldName(string propertyName) => propertyName switch
    {
        nameof(CreateCompanyRequest.Name) => "name",
        nameof(CreateCompanyRequest.WebsiteUrl) => "websiteUrl",
        nameof(CreateCompanyRequest.Industry) => "industry",
        nameof(CreateCompanyRequest.Location) => "location",
        nameof(CreateCompanyRequest.EmployeeCount) => "employeeCount",
        nameof(CreateCompanyRequest.Description) => "description",
        nameof(CreateCompanyRequest.CompanyType) => "companyType",
        _ => propertyName
    };

    private static string JobFieldName(string propertyName) => propertyName switch
    {
        nameof(CreateJobRequest.Title) => "title",
        nameof(CreateJobRequest.CompanyId) => "companyName",
        nameof(CreateJobRequest.CategoryId) => "categoryName",
        nameof(CreateJobRequest.Description) => "description",
        nameof(CreateJobRequest.ApplicationUrl) => "applicationUrl",
        nameof(CreateJobRequest.EmploymentType) => "employmentType",
        nameof(CreateJobRequest.WorkplaceType) => "workplaceType",
        nameof(CreateJobRequest.ExperienceLevel) => "experienceLevel",
        nameof(CreateJobRequest.Location) => "location",
        nameof(CreateJobRequest.MinimumSalary) => "minSalary",
        nameof(CreateJobRequest.MaximumSalary) => "maxSalary",
        nameof(CreateJobRequest.CurrencyCode) => "currencyCode",
        nameof(CreateJobRequest.Responsibilities) => "responsibilities",
        nameof(CreateJobRequest.Requirements) => "requirements",
        nameof(CreateJobRequest.Benefits) => "benefits",
        nameof(CreateJobRequest.MinimumExperienceYears) => "minExperienceYears",
        nameof(CreateJobRequest.MaximumExperienceYears) => "maxExperienceYears",
        nameof(CreateJobRequest.InternshipDurationMonths) => "internshipDurationMonths",
        nameof(CreateJobRequest.IsFlexibleDuration) => "isFlexibleDuration",
        nameof(CreateJobRequest.Department) => "department",
        nameof(CreateJobRequest.RoleCategory) => "roleCategory",
        nameof(CreateJobRequest.EducationRequirement) => "educationRequirement",
        nameof(CreateJobRequest.PostedByType) => "postedByType",
        _ => propertyName
    };

    private static CsvImportTemplate Template(string fileName, string content) =>
        new(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(content));

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record CompanyImportValues(
        string Name,
        string Slug,
        string? WebsiteUrl,
        string? Industry,
        string? Location,
        int? EmployeeCount,
        string? Description,
        CompanyType? CompanyType,
        bool WebsiteUrlSupplied,
        bool IndustrySupplied,
        bool LocationSupplied,
        bool EmployeeCountSupplied,
        bool DescriptionSupplied,
        bool CompanyTypeSupplied);

    private sealed record CompanyImportAction(
        int RowNumber,
        CompanyImportValues Values,
        Company? Existing);

    private sealed record JobImportAction(
        int RowNumber,
        CreateJobRequest Request);

    private abstract record ImportEvaluation<TAction>(
        int TotalRows,
        IReadOnlyCollection<TAction> Actions,
        IReadOnlyCollection<CsvImportRowResult> Rows,
        int DuplicateRows)
    {
        public int InvalidRows => Rows.Count(row => row.Status == "Invalid");

        public CsvImportResult ToResult() => new(
            TotalRows,
            Actions.Count,
            InvalidRows,
            DuplicateRows,
            0,
            DuplicateRows,
            InvalidRows == 0,
            Rows);

        public CsvImportResult ToCommittedResult(Func<TAction, string> status)
        {
            var committedStatuses = Actions.ToDictionary(
                RowNumber,
                status);
            var rows = Rows.Select(row => committedStatuses.TryGetValue(
                    row.RowNumber,
                    out var committedStatus)
                    ? row with { Status = committedStatus }
                    : row)
                .ToArray();
            return new(
                TotalRows,
                Actions.Count,
                InvalidRows,
                DuplicateRows,
                Actions.Count,
                DuplicateRows,
                true,
                rows);
        }

        protected abstract int RowNumber(TAction action);
    }

    private sealed record CompanyEvaluation(
        int TotalRows,
        IReadOnlyCollection<CompanyImportAction> Actions,
        IReadOnlyCollection<CsvImportRowResult> Rows,
        int DuplicateRows) :
        ImportEvaluation<CompanyImportAction>(
            TotalRows,
            Actions,
            Rows,
            DuplicateRows)
    {
        protected override int RowNumber(CompanyImportAction action) =>
            action.RowNumber;
    }

    private sealed record JobEvaluation(
        int TotalRows,
        IReadOnlyCollection<JobImportAction> Actions,
        IReadOnlyCollection<CsvImportRowResult> Rows,
        int DuplicateRows) :
        ImportEvaluation<JobImportAction>(
            TotalRows,
            Actions,
            Rows,
            DuplicateRows)
    {
        protected override int RowNumber(JobImportAction action) =>
            action.RowNumber;
    }
}
