using Microsoft.Net.Http.Headers;

namespace Anichron.API.Infrastructure;

public static class SecurityHeaders
{
    private const string appPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; media-src 'self' blob:; "
        + "connect-src 'self'; worker-src 'self'; manifest-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; "
        + "form-action 'self'; frame-ancestors 'none'; require-trusted-types-for 'script'";

    private const string apiPolicy = "frame-ancestors 'none'";

    private const string appPermissions =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    extension(WebApplication app)
    {
        public WebApplication UseSecurityHeaders()
        {
            app.Use((context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    Apply(context);
                    return Task.CompletedTask;
                });
                return next(context);
            });
            return app;
        }
    }

    private static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        if (context.Request.Path.StartsWithSegments(ApiPaths.Root))
        {
            headers[HeaderNames.ContentSecurityPolicy] = apiPolicy;
            return;
        }

        headers[HeaderNames.ContentSecurityPolicy] = appPolicy;
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = appPermissions;
    }
}
