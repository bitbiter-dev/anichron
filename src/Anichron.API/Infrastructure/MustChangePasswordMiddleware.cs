using Anichron.API.Security;

namespace Anichron.API.Infrastructure;

internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly (string Method, PathString Path)[] ExemptRoutes =
    [
        (HttpMethods.Get,  new(ApiPaths.Users.MePath)),
        (HttpMethods.Post, new(ApiPaths.Users.ChangePasswordPath)),
        (HttpMethods.Post, new(ApiPaths.Auth.LogoutPath)),
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(AppClaimTypes.MustChangePassword, "true")
            && !IsExemptRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = AuthMessages.MustChangePassword });
            return;
        }

        await next(context);
    }

    private static bool IsExemptRequest(HttpRequest request)
        => ExemptRoutes.Any(e => e.Method == request.Method && e.Path == request.Path);
}
