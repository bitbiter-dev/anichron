namespace Anichron.API.Infrastructure;

internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly PathString ChangePasswordPath =
        new($"{ApiPaths.Base}/{ApiPaths.Users.Group}{ApiPaths.Users.ChangePassword}");

    private static readonly PathString LogoutPath =
        new($"{ApiPaths.Base}/{ApiPaths.Users.Group}{ApiPaths.Auth.Logout}");

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(AppClaimTypes.MustChangePassword, "true")
            && !IsExemptPostRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = AuthMessages.MustChangePassword });
            return;
        }

        await next(context);
    }

    private static bool IsExemptPostRequest(HttpRequest request)
    {
        return request.Method == HttpMethods.Post
            && (request.Path == ChangePasswordPath || request.Path == LogoutPath);
    }
}
