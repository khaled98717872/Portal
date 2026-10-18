using Portal.SharedKernel.Localization;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Portal.Identity;
using Portal.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddPortalLocalization();
builder.Services.AddAuthorization();

builder.Services.AddDbContext<IdentityDb>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Identity"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDb.Schema));
    options.UseOpenIddict();
});

builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.ClaimsIdentity.UserNameClaimType = Claims.Name;
        options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
        options.ClaimsIdentity.RoleClaimType = Claims.Role;
        options.ClaimsIdentity.EmailClaimType = Claims.Email;
    })
    .AddEntityFrameworkStores<IdentityDb>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options => options.LoginPath = "/account/login");

builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<IdentityDb>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token")
               .SetEndSessionEndpointUris("/connect/logout");

        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow();

        options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess, AdminApi.Scope);

        options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();

        var aspNet = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough();

        if (builder.Environment.IsDevelopment())
            aspNet.DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
    await ClientSeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
}

app.UsePortalLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/", async (IdentityDb db) => Results.Ok(new
{
    service = "Portal.Identity",
    users = await db.Users.CountAsync()
}));

app.MapAdminApi();
app.MapCultureEndpoint();

app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post],
    async (HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
{
    var request = http.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

    var result = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
    if (!result.Succeeded)
    {
        var parameters = http.Request.HasFormContentType
            ? http.Request.Form.ToList()
            : http.Request.Query.ToList();

        return Results.Challenge(
            properties: new AuthenticationProperties
            {
                RedirectUri = http.Request.PathBase + http.Request.Path + QueryString.Create(parameters)
            },
            authenticationSchemes: [IdentityConstants.ApplicationScheme]);
    }

    var user = await users.GetUserAsync(result.Principal)
        ?? throw new InvalidOperationException("The user details cannot be retrieved.");

    var principal = await signIn.CreateUserPrincipalAsync(user);
    principal.SetScopes(request.GetScopes());
    principal.SetClaim(OpenIdHelpers.TenantClaim, user.TenantId.ToString());

    foreach (var claim in principal.Claims)
        claim.SetDestinations(OpenIdHelpers.GetDestinations(claim, principal));

    return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

app.MapPost("/connect/token", async (HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
{
    var request = http.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

    if (request.IsClientCredentialsGrantType())
    {
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, request.ClientId);
        identity.SetClaim(Claims.Name, request.ClientId);

        var clientPrincipal = new ClaimsPrincipal(identity);
        clientPrincipal.SetScopes(request.GetScopes());
        identity.SetDestinations(static claim => [Destinations.AccessToken]);

        return Results.SignIn(clientPrincipal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        throw new InvalidOperationException("The specified grant type is not supported.");

    var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    var principal = result.Principal!;

    var user = await users.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
    if (user is null || !await signIn.CanSignInAsync(user))
        return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

app.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post],
    async (SignInManager<AppUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
});

app.Run();

