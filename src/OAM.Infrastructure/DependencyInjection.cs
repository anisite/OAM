using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OAM.Domain.Interfaces;
using OAM.Infrastructure.Data;
using OAM.Infrastructure.Repositories;

namespace OAM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOamInfrastructure(this IServiceCollection services, string connectionString, bool useSqlite = false)
    {
        services.AddDbContext<OamDbContext>(options =>
        {
            if (useSqlite)
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(OamDbContext).Assembly.FullName);
                    sql.EnableRetryOnFailure(3);
                });
            }
        });

        services.AddScoped<IDefinitionWorkflowRepository, DefinitionWorkflowRepository>();
        services.AddScoped<IInstanceWorkflowRepository, InstanceWorkflowRepository>();
        services.AddScoped<IExecutionTacheRepository, ExecutionTacheRepository>();

        return services;
    }
}
