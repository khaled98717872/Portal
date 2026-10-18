using Portal.SharedKernel.Localization;
using Portal.SharedKernel.Authorization;
using Portal.SharedKernel.Tenancy;
using Portal.Tenancy;
using Portal.Host;
using Portal.Host.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPortalTenancy(builder.Configuration);
builder.Services.AddPortalLocalization();
builder.Services.AddPortalAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddPortalPermissions(ModuleCatalog.Modules);
builder.Services.AddPortalIdentityAdmin(builder.Configuration);
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, TenantCircuitHandler>();

foreach (var module in ModuleCatalog.Modules)
    module.RegisterServices(builder.Services, builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var seedScope = app.Services.CreateScope();
    await TenancySeeder.SeedAsync(seedScope.ServiceProvider, app.Configuration);
}
app.UsePortalLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenancy();
app.UseModuleAccess(ModuleCatalog.Modules.Select(m => m.Name));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(ModuleCatalog.UiAssemblies);

foreach (var module in ModuleCatalog.Modules)
    module.MapEndpoints(app);

app.MapGet("/api/tenant/whoami", (ITenantContext t) => t.IsResolved ? Results.Ok(new { t.TenantId, t.Identifier, t.Isolation }) : Results.NotFound(new { error = "No tenant resolved" }));

app.MapPortalAuth();
app.MapCultureEndpoint();

app.Run();







