using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobPortal.Persistence.Context;

public class JobPortalDbContextFactory : IDesignTimeDbContextFactory<JobPortalDbContext>
{
    public JobPortalDbContext CreateDbContext(string[] args)
    {
        // This connection string is ONLY used for migrations. 
        // Replace the values below to match your actual SQL Server connection details.
        var optionsBuilder = new DbContextOptionsBuilder<JobPortalDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=JobPortalDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");

        return new JobPortalDbContext(optionsBuilder.Options);
    }
}
