using Anichron.API.Endpoints;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Anichron.API.Infrastructure;

public static class ApplicationExtensions
{
    extension(WebApplication app)
    {
        public WebApplication UseApiProblems()
        {
            app.UseExceptionHandler();
            app.UseWhen(
                context => context.Request.Path.StartsWithSegments(ApiPaths.Root),
                api => api.UseStatusCodePages());
            return app;
        }

        public WebApplication MapApiEndpoints()
        {
            app.MapHealthChecks(ApiPaths.Healthz, new HealthCheckOptions
            {
                ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
            }).AllowAnonymous();

            var api = app.MapGroup(ApiPaths.Base);
            ((IEndpointConventionBuilder)api).Finally(EndpointResponses.DescribeErrorsAsProblems);
            api.MapAuthEndpoints();
            api.MapUserEndpoints();
            api.MapAdminEndpoints();
            api.MapMediaEndpoints();
            api.MapFlashbackEndpoints();
            api.MapBurstEndpoints();
            api.MapWorkItemEndpoints();
            api.MapProxySweepEndpoints();
            return app;
        }
    }
}
