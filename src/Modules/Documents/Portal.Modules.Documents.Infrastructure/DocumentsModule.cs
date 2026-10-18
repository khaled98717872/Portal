using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.SharedKernel.Modules;

namespace Portal.Modules.Documents.Infrastructure;

public sealed class DocumentsModule : IModule
{
    public string Name => "Documents";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents").RequireAuthorization();
        group.MapGet("/ping", () => Results.Ok(new { module = "Documents", status = "ok" }));
    }
}


