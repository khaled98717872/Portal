using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Portal.SharedKernel.Authorization;
using Portal.SharedKernel.Tenancy;

namespace Portal.Host;

public sealed record RoleInfo(string Name, List<string> Permissions, bool IsSystem);
public sealed record UserInfo(Guid Id, string Email, string? FullName, bool IsActive, List<string> Roles);

public sealed class IdentityTokenProvider(IHttpClientFactory factory, IConfiguration config)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expires) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expires) return _token;

            var authority = (config["Oidc:Authority"] ?? "http://localhost:5200").TrimEnd('/');
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = config["IdentityAdmin:ClientId"] ?? "portal-host-admin",
                ["client_secret"] = config["IdentityAdmin:ClientSecret"]
                    ?? throw new InvalidOperationException("IdentityAdmin:ClientSecret is missing (use user-secrets)."),
                ["scope"] = "identity.admin"
            };

            using var response = await factory.CreateClient()
                .PostAsync($"{authority}/connect/token", new FormUrlEncodedContent(form), ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Could not obtain an admin token from the identity service.");

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                       ?? throw new InvalidOperationException("Empty token response from the identity service.");

            _token = body.AccessToken;
            _expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(body.ExpiresIn - 60, 30));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed class IdentityAdminClient(HttpClient http, IdentityTokenProvider tokens)
{
    public Task<List<RoleInfo>> GetRolesAsync(CancellationToken ct = default) =>
        GetAsync<List<RoleInfo>>("roles", ct);

    public Task SaveRoleAsync(string name, IEnumerable<string> permissions, CancellationToken ct = default) =>
        SendCheckedAsync(HttpMethod.Put, $"roles/{Uri.EscapeDataString(name)}", new { permissions }, ct);

    public Task DeleteRoleAsync(string name, CancellationToken ct = default) =>
        SendCheckedAsync(HttpMethod.Delete, $"roles/{Uri.EscapeDataString(name)}", null, ct);

    public Task<List<UserInfo>> GetUsersAsync(Guid tenantId, CancellationToken ct = default) =>
        GetAsync<List<UserInfo>>($"tenants/{tenantId}/users", ct);

    public Task CreateUserAsync(Guid tenantId, string email, string? fullName, string password,
        IEnumerable<string> roles, CancellationToken ct = default) =>
        SendCheckedAsync(HttpMethod.Post, $"tenants/{tenantId}/users", new { email, fullName, password, roles }, ct);

    public Task UpdateUserAsync(Guid tenantId, Guid userId, string? fullName, bool isActive,
        IEnumerable<string> roles, CancellationToken ct = default) =>
        SendCheckedAsync(HttpMethod.Put, $"tenants/{tenantId}/users/{userId}", new { fullName, isActive, roles }, ct);

    public Task ResetPasswordAsync(Guid tenantId, Guid userId, string newPassword, CancellationToken ct = default) =>
        SendCheckedAsync(HttpMethod.Post, $"tenants/{tenantId}/users/{userId}/password", new { newPassword }, ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct)
               ?? throw new InvalidOperationException("Empty response from the identity service.");
    }

    private async Task SendCheckedAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, body, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ErrorBody>(ct))?.Error; }
        catch { /* not a JSON error body */ }

        throw new InvalidOperationException(message ?? $"The identity service returned {(int)response.StatusCode}.");
    }

    private sealed record ErrorBody(string? Error);
}

public sealed class UserAdminService(IdentityAdminClient client, AuthenticationStateProvider auth)
{
    private async Task<ClaimsPrincipal> AuthorizeAsync(Guid tenantId)
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;

        if (PermissionMatcher.IsGranted(user, PlatformPermissions.UsersManage)) return user;

        var own = Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (own != Guid.Empty && own == tenantId
            && PermissionMatcher.IsGranted(user, IdentityPermissions.UsersManage))
            return user;

        throw new UnauthorizedAccessException("You are not allowed to manage users of this tenant.");
    }

    public async Task<List<UserInfo>> ListUsersAsync(Guid tenantId, CancellationToken ct = default)
    {
        await AuthorizeAsync(tenantId);
        return await client.GetUsersAsync(tenantId, ct);
    }

    public async Task<List<RoleInfo>> ListAssignableRolesAsync(Guid tenantId, CancellationToken ct = default)
    {
        await AuthorizeAsync(tenantId);
        var roles = await client.GetRolesAsync(ct);

        return tenantId == PlatformTenant.Id
            ? roles
            : roles.Where(r => !r.Permissions.Any(p => p.StartsWith("platform.", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public async Task CreateAsync(Guid tenantId, string email, string? fullName, string password,
        IEnumerable<string> roles, CancellationToken ct = default)
    {
        await AuthorizeAsync(tenantId);
        await client.CreateUserAsync(tenantId, email, fullName, password, roles, ct);
    }

    public async Task UpdateAsync(Guid tenantId, Guid userId, string? fullName, bool isActive,
        IEnumerable<string> roles, CancellationToken ct = default)
    {
        var actor = await AuthorizeAsync(tenantId);
        if (actor.FindFirst("sub")?.Value == userId.ToString())
            throw new InvalidOperationException("You cannot modify your own account here.");

        await client.UpdateUserAsync(tenantId, userId, fullName, isActive, roles, ct);
    }

    public async Task ResetPasswordAsync(Guid tenantId, Guid userId, string newPassword, CancellationToken ct = default)
    {
        await AuthorizeAsync(tenantId);
        await client.ResetPasswordAsync(tenantId, userId, newPassword, ct);
    }
}

public sealed class RoleAdminService(IdentityAdminClient client, AuthenticationStateProvider auth)
{
    private async Task EnsureAllowedAsync()
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        if (!PermissionMatcher.IsGranted(user, PlatformPermissions.RolesManage))
            throw new UnauthorizedAccessException("You are not allowed to manage roles.");
    }

    public async Task<List<RoleInfo>> ListAsync(CancellationToken ct = default)
    {
        await EnsureAllowedAsync();
        return await client.GetRolesAsync(ct);
    }

    public async Task SaveAsync(string name, IEnumerable<string> permissions, CancellationToken ct = default)
    {
        await EnsureAllowedAsync();
        await client.SaveRoleAsync(name, permissions, ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await EnsureAllowedAsync();
        await client.DeleteRoleAsync(name, ct);
    }
}

public static class IdentityAdminExtensions
{
    public static IServiceCollection AddPortalIdentityAdmin(this IServiceCollection services, IConfiguration config)
    {
        var authority = (config["Oidc:Authority"] ?? "http://localhost:5200").TrimEnd('/');

        services.AddHttpClient();
        services.AddSingleton<IdentityTokenProvider>();
        services.AddHttpClient<IdentityAdminClient>(client => client.BaseAddress = new Uri(authority + "/api/admin/"));
        services.AddScoped<UserAdminService>();
        services.AddScoped<RoleAdminService>();
        return services;
    }
}
