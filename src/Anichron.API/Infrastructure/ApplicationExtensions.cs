namespace Anichron.API.Infrastructure;

public static class ApplicationExtensions
{
    extension(WebApplication app)
    {
        public WebApplication UseApiProblems()
        {
            app.UseExceptionHandler();
            app.UseWhen(
                context => ApiPaths.IsApi(context.Request.Path),
                api => api.UseStatusCodePages());
            return app;
        }
    }
}
