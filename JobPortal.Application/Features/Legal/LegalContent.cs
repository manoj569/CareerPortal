namespace JobPortal.Application.Features.Legal;

public sealed record LegalDocumentResponse(
    string Version,
    string Title,
    DateOnly EffectiveDate,
    string Content,
    string ContentType);

public static class LegalDocumentCatalog
{
    public const string CurrentVersion = "2026-08-01";
    private static readonly DateOnly EffectiveDate = new(2026, 8, 1);

    public static LegalDocumentResponse TermsOfUse() => new(
        CurrentVersion,
        "Terms of Use",
        EffectiveDate,
        "By creating an account, you agree to provide accurate information, keep your credentials secure, use the Career Portal lawfully, and comply with applicable job-application and membership rules. The service may restrict accounts used for abuse, fraud, or unauthorized access.",
        "text/plain");

    public static LegalDocumentResponse PrivacyPolicy() => new(
        CurrentVersion,
        "Privacy Policy",
        EffectiveDate,
        "The Career Portal processes account, profile, application, membership, and security data to provide the service. Passwords and one-time passwords are never stored in plain text. Access is role-scoped, and sensitive credentials belong only in approved secret stores. Contact the service administrator for retention or account-data requests.",
        "text/plain");
}
