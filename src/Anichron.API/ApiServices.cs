using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;

namespace Anichron.API;

/// <summary>The services of the API's HTTP surface: the host plumbing in <c>Infrastructure</c> plus what the endpoints use.</summary>
public static class ApiServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApiSurface(IConfiguration configuration)
            => services
                .AddOpenApi(options => options.AddSchemaTransformer<ProblemCodeSchemaTransformer>())
                .AddApiProblems()
                .AddDatabase(configuration)
                .AddForwardedHeadersSupport(configuration)
                .AddAuthServices(configuration)
                .AddWorkItemServices()
                .AddAuthorizationPolicies()
                .AddRateLimiting(configuration)
                .AddCorsPolicy(configuration)
                .AddApiHealthChecks()
                .AddEndpointServices();
    }
}
