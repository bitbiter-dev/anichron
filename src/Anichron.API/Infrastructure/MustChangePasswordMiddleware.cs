using Anichron.API.Security;

namespace Anichron.API.Infrastructure;

internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly (string Method, PathString Path)[] ExemptRoutes =
    [
        (HttpMethods.Get,  new($"{ApiPaths.Base}/{ApiPaths.Users.Group}{ApiPaths.Users.Me}")),
        (HttpMethods.Post, new($"{ApiPaths.Base}/{ApiPaths.Users.Group}{ApiPaths.Users.ChangePassword}")),
        (HttpMethods.Post, new($"{ApiPaths.Base}/{ApiPaths.Auth.Group}{ApiPaths.Auth.Logout}")),
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
