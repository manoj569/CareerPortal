using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Infrastructure.Authentication;
using JobPortal.Infrastructure.Payments;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobPortal.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton<IRazorpayGateway, RazorpayGateway>();
        services.AddSingleton<IMembershipPlanProvider, ConfigurationMembershipPlanProvider>();
        return services;
    }
}
