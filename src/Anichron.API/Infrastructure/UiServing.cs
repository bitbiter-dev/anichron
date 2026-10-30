using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Anichron.API.Infrastructure;

public static class UiServing
{
    private const string hashedAssets = "/assets";
    private const string index = "/index.html";

    extension(WebApplication app)
    {
        public WebApplication UseUi()
        {
            app.Use((context, next) =>
            {
                if (context.GetEndpoint() is not null)
                    return next(context);
                if (context.Request.Path.StartsWithSegments(ApiPaths.Root))
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
                if (IsAppRoute(context.Request))
                    context.Request.Path = index;
                return next(context);
            });
            app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = SetCacheControl });
            return app;
        }
    }

    private static bool IsAppRoute(HttpRequest request)
        => (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && !Path.HasExtension(request.Path.Value);

    private static void SetCacheControl(StaticFileResponseContext context)
    {
        context.Context.Response.Headers[HeaderNames.CacheControl] =
            context.Context.Request.Path.StartsWithSegments(hashedAssets)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    }
}
