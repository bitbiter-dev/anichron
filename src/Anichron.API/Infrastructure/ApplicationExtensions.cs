using Anichron.API.Endpoints;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Anichron.API.Infrastructure;

public static class ApplicationExtensions
{
    extension(WebApplication app)
    {
        public WebApplication MapApiEndpoints()
        {
            app.MapHealthChecks(ApiPaths.Healthz, new HealthCheckOptions
            {
                ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
            }).AllowAnonymous();

            var api = app.MapGroup(ApiPaths.Base);
            api.MapAuthEndpoints();
            api.MapUserEndpoints();
            api.MapAdminEndpoints();
            return app;
        }
    }
}
