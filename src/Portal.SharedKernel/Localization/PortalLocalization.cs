using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Portal.SharedKernel.Localization;

public static class PortalLocalization
{
    public const string Arabic = "ar-OM";
    public const string English = "en-GB";

    public static readonly string[] SupportedCultures = [Arabic, English];

    public static IServiceCollection AddPortalLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(Arabic)
                   .AddSupportedCultures(SupportedCultures)
                   .AddSupportedUICultures(SupportedCultures);

            // Only the cookie decides the language, so the default stays predictable.
            options.RequestCultureProviders.Clear();
            options.RequestCultureProviders.Add(new CookieRequestCultureProvider());
        });

        return services;
    }

    public static IApplicationBuilder UsePortalLocalization(this IApplicationBuilder app) =>
        app.UseRequestLocalization();

    public static IEndpointRouteBuilder MapCultureEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/culture/set", (string culture, string? redirectUri, HttpContext http) =>
        {
            var supported = SupportedCultures.FirstOrDefault(
                c => c.Equals(culture, StringComparison.OrdinalIgnoreCase));

            if (supported is null)
                return Results.BadRequest(new { error = "Unsupported culture." });

            http.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(supported)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });

            var target = redirectUri is { Length: > 0 }
                         && redirectUri.StartsWith('/')
                         && !redirectUri.StartsWith("//", StringComparison.Ordinal)
                         && !redirectUri.StartsWith("/\\", StringComparison.Ordinal)
                ? redirectUri
                : "/";

            return Results.LocalRedirect(target);
        }).AllowAnonymous();

        return endpoints;
    }
}
