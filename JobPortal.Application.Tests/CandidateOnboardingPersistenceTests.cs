using JobPortal.Application.Features.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateOnboardingPersistenceTests
{
    [Fact]
    public void UserModelHasUniqueNormalizedEmailAndMobileIndexes()
    {
        using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var user = context.Model.FindEntityType(typeof(User))
            ?? throw new InvalidOperationException("User metadata was not found.");
        var indexes = user.GetIndexes().ToArray();
        var email = Assert.Single(indexes, index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(User.NormalizedEmail)]));
        var mobile = Assert.Single(indexes, index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(User.NormalizedPhoneNumber)]));

        Assert.True(email.IsUnique);
        Assert.True(mobile.IsUnique);
        Assert.Contains(
            nameof(User.NormalizedPhoneNumber),
            mobile.GetFilter(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationBackfillsCanonicalMobileSafelyBeforeCreatingUniqueIndex()
    {
        var migrations = Path.Combine(FindRepositoryRoot(), "JobPortal.Persistence", "Migrations");
        var migrationPath = Directory
            .GetFiles(migrations, "*_AddCandidateRegistrationAndOnboarding.cs")
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("RankedNumbers", migration, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER()", migration, StringComparison.Ordinal);
        Assert.Contains("NormalizedPhoneNumber", migration, StringComparison.Ordinal);
        Assert.Contains("[PhoneNumber] = NULL", migration, StringComparison.Ordinal);
        Assert.Contains("unique: true", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicRegistrationAndOnboardingContractsExposeNoPrivilegedFields()
    {
        Assert.Equal(
            [
                "FullName",
                "Email",
                "Password",
                "PhoneNumber",
                "HasAcceptedTermsAndPrivacy"
            ],
            typeof(RegisterRequest).GetProperties().Select(property => property.Name));

        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            root, "JobPortal.API", "Controllers", "CandidateController.cs"));
        Assert.Contains(
            "[Authorize(Roles = \"Candidate\")]",
            controller,
            StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"onboarding\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPut(\"onboarding\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", controller, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
