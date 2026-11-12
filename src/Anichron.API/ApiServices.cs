using Anichron.API.Endpoints;
using Anichron.API.Infrastructure;
using System.Text.Json.Serialization;

namespace Anichron.API;

/// <summary>The services of the API's HTTP surface: the host plumbing in <c>Infrastructure</c> plus what the endpoints use.</summary>
public static class ApiServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApiSurface(IConfiguration configuration)
            => services
                .ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict)
                .AddOpenApi(options => options
                    .AddDocumentTransformer<ProblemCodeDocumentTransformer>()
                    .AddDocumentTransformer<WorkItemEnumDocumentTransformer>())
                .AddApiProblems()
                .AddDatabase(configuration)
                .AddForwardedHeadersSupport(configuration)
                .AddAuthServices(configuration)
                .AddProxyEndpoints()
                .AddWorkItemServices()
                .AddAuthorizationPolicies()
                .AddRateLimiting(configuration)
                .AddCorsPolicy(configuration)
                .AddApiHealthChecks()
                .AddEndpointServices();
    }
}
