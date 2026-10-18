using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy.Data;

namespace Portal.Tenancy;

public static class TenancyExtensions
{
    public static IServiceCollection AddPortalTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        services.AddDbContext<TenancyDb>(options => options.UseNpgsql(
            configuration.GetConnectionString("Shared"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TenancyDb.Schema)));

        services.AddScoped<ITenantStore, DbTenantStore>();
        services.AddScoped<IModuleAccessService, ModuleAccessService>();
        services.AddScoped<TenantAdminService>();
        services.AddScoped<TenantContext>();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<Portal.SharedKernel.Auditing.ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantConnectionResolver, TenantConnectionResolver>();
        return services;
    }

    public static IApplicationBuilder UseTenancy(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}


