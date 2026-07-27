using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobPortal.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContextPool<JobPortalDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer =>
            {
                sqlServer.CommandTimeout(30);
                sqlServer.MaxBatchSize(100);
                sqlServer.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
            }), poolSize: 128);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IPublicJobRepository, PublicJobRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IAdminDashboardRepository, AdminDashboardRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
