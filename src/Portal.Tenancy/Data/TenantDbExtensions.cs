using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Portal.Tenancy.Data;

public static class TenantDbExtensions
{
    public static IServiceCollection AddTenantDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : TenantDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            var connection = sp.GetRequiredService<ITenantConnectionResolver>().Resolve();
            options.UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        });
        return services;
    }
}
