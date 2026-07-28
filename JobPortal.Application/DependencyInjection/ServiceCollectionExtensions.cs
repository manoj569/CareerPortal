using FluentValidation;
using JobPortal.Application.Abstractions.AdminDashboard;
using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Dashboard;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.AdminDashboard;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Application.Features.PublicJobs;
using Microsoft.Extensions.DependencyInjection;

namespace JobPortal.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<AdminBootstrapService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IPublicJobService, PublicJobService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<ICompanyManagementService, CompanyManagementService>();
        services.AddScoped<ICategoryManagementService, CategoryManagementService>();
        return services;
    }
}
