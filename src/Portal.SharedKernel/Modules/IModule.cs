using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.SharedKernel.Authorization;

namespace Portal.SharedKernel.Modules;

public interface IModule
{
    string Name { get; }
    IReadOnlyCollection<PermissionDefinition> Permissions => [];
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
