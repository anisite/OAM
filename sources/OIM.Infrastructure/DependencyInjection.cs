using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OIM.Domain.Interfaces;
using OIM.Infrastructure.Data;
using OIM.Infrastructure.Repositories;

namespace OIM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOimInfrastructure(this IServiceCollection services, string connectionString, bool useSqlite = false)
    {
        services.AddDbContext<OimDbContext>(options =>
        {
            if (useSqlite)
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(OimDbContext).Assembly.FullName);
                    sql.EnableRetryOnFailure(3);
                });
            }
        });

        services.AddScoped<IDefinitionWorkflowRepository, DefinitionWorkflowRepository>();
        services.AddScoped<IInstanceWorkflowRepository, InstanceWorkflowRepository>();
        services.AddScoped<IExecutionTacheRepository, ExecutionTacheRepository>();
        services.AddScoped<ICasTestRepository, CasTestRepository>();

        return services;
    }
}
