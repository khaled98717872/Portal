using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Portal.Identity.Data;

namespace Portal.Identity;

public sealed record RoleDto(string Name, List<string> Permissions, bool IsSystem);
public sealed record RoleRequest(List<string>? Permissions);
public sealed record UserDto(Guid Id, string Email, string? FullName, bool IsActive, List<string> Roles);
public sealed record CreateUserRequest(string? Email, string? FullName, string? Password, List<string>? Roles);
public sealed record UpdateUserRequest(string? FullName, bool IsActive, List<string>? Roles);
public sealed record PasswordRequest(string? NewPassword);

public static class AdminApi
{
    public const string Scope = "identity.admin";

    private const string PermissionClaimType = "permission";
    private static readonly Guid PlatformTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static readonly HashSet<string> SystemRoles =
        new(new[] { "TenantAdmin", "PlatformAdmin" }, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex RoleNamePattern = new("^[A-Za-z][A-Za-z0-9_-]{1,63}$", RegexOptions.Compiled);
    private static readonly Regex PermissionPattern = new(@"^[a-z][a-z0-9]*(\.[a-z0-9]+)*(\.\*)?$", RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/admin")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => ctx.User.HasScope(Scope)));

        // ---------- Roles ----------

        api.MapGet("/roles", async (RoleManager<IdentityRole<Guid>> roles) =>
        {
            var result = new List<RoleDto>();
            foreach (var role in await roles.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync())
                result.Add(new RoleDto(role.Name!, await PermissionsOf(roles, role), SystemRoles.Contains(role.Name!)));
            return Results.Ok(result);
        });

        api.MapPut("/roles/{name}", async (string name, RoleRequest request, RoleManager<IdentityRole<Guid>> roles) =>
        {
            name = name.Trim();
            if (!RoleNamePattern.IsMatch(name))
                return Results.BadRequest(new { error = "Role name: 2-64 characters, letters, digits, '-' and '_', starting with a letter." });
            if (SystemRoles.Contains(name))
                return Results.BadRequest(new { error = $"'{name}' is a system role and cannot be modified." });

            var permissions = (request.Permissions ?? [])
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var invalid = permissions.FirstOrDefault(p => !PermissionPattern.IsMatch(p));
            if (invalid is not null)
                return Results.BadRequest(new { error = $"Invalid permission '{invalid}'." });

            var role = await roles.FindByNameAsync(name);
            if (role is null)
            {
                role = new IdentityRole<Guid>(name);
                var created = await roles.CreateAsync(role);
                if (!created.Succeeded) return Results.BadRequest(Errors(created));
            }

            var current = (await roles.GetClaimsAsync(role)).Where(c => c.Type == PermissionClaimType).ToList();

            foreach (var claim in current.Where(c => !permissions.Contains(c.Value, StringComparer.OrdinalIgnoreCase)))
                await roles.RemoveClaimAsync(role, claim);

            foreach (var permission in permissions.Where(p => !current.Any(c => c.Value.Equals(p, StringComparison.OrdinalIgnoreCase))))
                await roles.AddClaimAsync(role, new Claim(PermissionClaimType, permission));

            return Results.Ok(new RoleDto(role.Name!, permissions, false));
        });

        api.MapDelete("/roles/{name}", async (string name, RoleManager<IdentityRole<Guid>> roles, UserManager<AppUser> users) =>
        {
            if (SystemRoles.Contains(name))
                return Results.BadRequest(new { error = $"'{name}' is a system role and cannot be deleted." });

            var role = await roles.FindByNameAsync(name);
            if (role is null) return Results.NotFound();

            if ((await users.GetUsersInRoleAsync(name)).Count > 0)
                return Results.Conflict(new { error = "The role is assigned to users. Remove it from them first." });

            var deleted = await roles.DeleteAsync(role);
            return deleted.Succeeded ? Results.NoContent() : Results.BadRequest(Errors(deleted));
        });

        // ---------- Users (scoped to a tenant) ----------

        var tenantUsers = api.MapGroup("/tenants/{tenantId:guid}/users");

        tenantUsers.MapGet("/", async (Guid tenantId, UserManager<AppUser> users) =>
        {
            var list = await users.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.Email)
                .ToListAsync();

            var result = new List<UserDto>();
            foreach (var user in list) result.Add(await ToDto(users, user));
            return Results.Ok(result);
        });

        tenantUsers.MapPost("/", async (Guid tenantId, CreateUserRequest request,
            UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles) =>
        {
            var email = request.Email?.Trim() ?? "";
            if (!new EmailAddressAttribute().IsValid(email))
                return Results.BadRequest(new { error = "A valid email is required." });
            if (string.IsNullOrEmpty(request.Password))
                return Results.BadRequest(new { error = "A password is required." });

            var (error, roleNames) = await ValidateRolesAsync(roles, tenantId, request.Roles ?? []);
            if (error is not null) return Results.BadRequest(new { error });

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = request.FullName?.Trim(),
                TenantId = tenantId
            };

            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded) return Results.BadRequest(Errors(created));

            if (roleNames.Count > 0)
            {
                var added = await users.AddToRolesAsync(user, roleNames);
                if (!added.Succeeded) return Results.BadRequest(Errors(added));
            }

            return Results.Created($"/api/admin/tenants/{tenantId}/users/{user.Id}", await ToDto(users, user));
        });

        tenantUsers.MapPut("/{id:guid}", async (Guid tenantId, Guid id, UpdateUserRequest request,
            UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles) =>
        {
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null || user.TenantId != tenantId) return Results.NotFound();

            var (error, roleNames) = await ValidateRolesAsync(roles, tenantId, request.Roles ?? []);
            if (error is not null) return Results.BadRequest(new { error });

            user.FullName = request.FullName?.Trim();
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded) return Results.BadRequest(Errors(updated));

            if (request.IsActive)
            {
                await users.SetLockoutEndDateAsync(user, null);
                await users.ResetAccessFailedCountAsync(user);
            }
            else
            {
                await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            }

            var current = await users.GetRolesAsync(user);
            var toRemove = current.Where(r => !roleNames.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
            var toAdd = roleNames.Where(r => !current.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();

            if (toRemove.Count > 0) await users.RemoveFromRolesAsync(user, toRemove);
            if (toAdd.Count > 0) await users.AddToRolesAsync(user, toAdd);

            return Results.Ok(await ToDto(users, user));
        });

        tenantUsers.MapPost("/{id:guid}/password", async (Guid tenantId, Guid id, PasswordRequest request,
            UserManager<AppUser> users) =>
        {
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null || user.TenantId != tenantId) return Results.NotFound();
            if (string.IsNullOrEmpty(request.NewPassword))
                return Results.BadRequest(new { error = "A new password is required." });

            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, request.NewPassword);
            return reset.Succeeded ? Results.NoContent() : Results.BadRequest(Errors(reset));
        });

        return app;
    }

    private static async Task<List<string>> PermissionsOf(RoleManager<IdentityRole<Guid>> roles, IdentityRole<Guid> role) =>
        (await roles.GetClaimsAsync(role))
            .Where(c => c.Type == PermissionClaimType)
            .Select(c => c.Value)
            .OrderBy(v => v)
            .ToList();

    private static async Task<UserDto> ToDto(UserManager<AppUser> users, AppUser user) =>
        new(user.Id,
            user.Email ?? "",
            user.FullName,
            !(user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow),
            (await users.GetRolesAsync(user)).OrderBy(r => r).ToList());

    // Roles that carry platform permissions can only be assigned to platform users.
    private static async Task<(string? Error, List<string> Names)> ValidateRolesAsync(
        RoleManager<IdentityRole<Guid>> roles, Guid tenantId, IEnumerable<string> requested)
    {
        var names = new List<string>();

        foreach (var requestedName in requested.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var role = await roles.FindByNameAsync(requestedName);
            if (role is null) return ($"Role '{requestedName}' does not exist.", names);

            var isPlatformRole = (await PermissionsOf(roles, role))
                .Any(p => p.StartsWith("platform.", StringComparison.OrdinalIgnoreCase));

            if (isPlatformRole && tenantId != PlatformTenantId)
                return ($"Role '{role.Name}' is a platform role and can only be assigned to platform users.", names);

            names.Add(role.Name!);
        }

        return (null, names);
    }

    private static object Errors(IdentityResult result) =>
        new { error = string.Join("; ", result.Errors.Select(e => e.Description)) };
}
