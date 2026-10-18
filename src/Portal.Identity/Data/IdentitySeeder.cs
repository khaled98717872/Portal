using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Portal.Identity.Data;

public static class IdentitySeeder
{
    private const string DevPassword = "Passw0rd!Dev1";
    private const string PermissionClaimType = "permission";

    private static readonly (string Role, string[] Permissions)[] Roles =
    [
        ("TenantAdmin", new[] { "*" }),
        ("HrManager", new[] { "hr.employees.read", "hr.employees.create", "hr.employees.update", "hr.employees.delete", "hr.audit.read" }),
        ("HrViewer", new[] { "hr.employees.read" }),
        ("PlatformAdmin", new[] { "platform.*" })
    ];

    public static async Task SeedAsync(IServiceProvider sp)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();

        foreach (var (name, permissions) in Roles)
        {
            var role = await roles.FindByNameAsync(name);
            if (role is null)
            {
                role = new IdentityRole<Guid>(name);
                var created = await roles.CreateAsync(role);
                if (!created.Succeeded)
                    throw new InvalidOperationException($"Creating role {name} failed: {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }

            var existing = (await roles.GetClaimsAsync(role))
                .Where(c => c.Type == PermissionClaimType)
                .Select(c => c.Value)
                .ToHashSet();

            foreach (var permission in permissions.Where(p => !existing.Contains(p)))
                await roles.AddClaimAsync(role, new Claim(PermissionClaimType, permission));
        }

        var moh = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var acme = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var beta = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var platform = Guid.Parse("99999999-9999-9999-9999-999999999999");

        (string Email, string Name, Guid Tenant, string Role)[] seed =
        [
            ("admin@moh.test", "MOH Admin", moh, "TenantAdmin"),
            ("admin@acme.test", "Acme Admin", acme, "TenantAdmin"),
            ("admin@beta.test", "Beta Admin", beta, "TenantAdmin"),
            ("viewer@acme.test", "Acme HR Viewer", acme, "HrViewer"),
            ("platform@portal.test", "Platform Admin", platform, "PlatformAdmin")
        ];

        foreach (var (email, name, tenant, role) in seed)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    FullName = name,
                    TenantId = tenant
                };

                var result = await users.CreateAsync(user, DevPassword);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Seeding {email} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            if (!await users.IsInRoleAsync(user, role))
                await users.AddToRoleAsync(user, role);
        }
    }
}
