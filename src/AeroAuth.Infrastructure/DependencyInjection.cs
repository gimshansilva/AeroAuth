using AeroAuth.Core.Interfaces;
using AeroAuth.Core.Services;
using AeroAuth.Infrastructure.Data;
using AeroAuth.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AeroAuth.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the connection factory, unit of work, repositories and the services that use them.</summary>
    public static IServiceCollection AddAeroAuthInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));

        // Scoped: one connection and one transaction per request, shared by every repository in that request.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<ITrainingSessionRepository, TrainingSessionRepository>();
        services.AddScoped<IReviewQueueRepository, ReviewQueueRepository>();
        services.AddScoped<ICertificateRepository, CertificateRepository>();
        services.AddScoped<ICounterRepository, CounterRepository>();
        services.AddScoped<IAdminActionRepository, AdminActionRepository>();
        services.AddScoped<IReferenceRepository, ReferenceRepository>();

        services.AddScoped<StaffService>();
        services.AddScoped<TrainingApprovalService>();
        return services;
    }
}
