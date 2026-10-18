using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Portal.SharedKernel.Modules;

namespace Portal.SharedKernel.Authorization;

public static class PermissionClaim
{
    public const string Type = "permission";
}

public sealed record PermissionDefinition(string Name, string Description);

public static class PlatformPermissions
{
    public const string TenantsManage = "platform.tenants.manage";
    public const string UsersManage = "platform.users.manage";
    public const string RolesManage = "platform.roles.manage";

    public static readonly IReadOnlyCollection<PermissionDefinition> All =
    [
        new(TenantsManage, "Manage tenants and subscriptions"),
        new(UsersManage, "Manage users of any tenant"),
        new(RolesManage, "Manage role definitions")
    ];
}

public static class IdentityPermissions
{
    public const string UsersManage = "identity.users.manage";

    // Policy: identity.users.manage OR platform.users.manage
    public const string UsersAdminPolicy = "policy.users-admin";

    public static readonly IReadOnlyCollection<PermissionDefinition> All =
    [
        new(UsersManage, "Manage users of the tenant")
    ];
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public static class PermissionMatcher
{
    public static bool IsGranted(ClaimsPrincipal user, string permission)
    {
        foreach (var claim in user.FindAll(PermissionClaim.Type))
        {
            var granted = claim.Value;

            if (granted == "*")
            {
                // "*" covers every module permission, but never platform-level ones.
                if (!permission.StartsWith("platform.", StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }

            if (granted.Equals(permission, StringComparison.OrdinalIgnoreCase))
                return true;

            // "hr.*" -> prefix "hr.", "hr.employees.*" -> prefix "hr.employees."
            if (granted.EndsWith(".*", StringComparison.Ordinal)
                && permission.StartsWith(granted[..^1], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (PermissionMatcher.IsGranted(context.User, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class PermissionExtensions
{
    public static IServiceCollection AddPortalPermissions(
        this IServiceCollection services, IEnumerable<IModule> modules)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

        var names = modules.SelectMany(m => m.Permissions)
            .Concat(PlatformPermissions.All)
            .Concat(IdentityPermissions.All)
            .Select(p => p.Name)
            .Distinct()
            .ToList();

        services.Configure<AuthorizationOptions>(options =>
        {
            foreach (var name in names)
            {
                options.AddPolicy(name, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(name)));
            }

            options.AddPolicy(IdentityPermissions.UsersAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                    PermissionMatcher.IsGranted(ctx.User, IdentityPermissions.UsersManage)
                    || PermissionMatcher.IsGranted(ctx.User, PlatformPermissions.UsersManage)));
        });

        return services;
    }
}
