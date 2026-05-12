namespace Anichron.API.Infrastructure;

internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly PathString ChangePasswordPath =
        new($"{ApiPaths.Base}/{ApiPaths.Users.Group}{ApiPaths.Users.ChangePassword}");

    private static readonly PathString LogoutPath =
        new($"{ApiPaths.Base}/{ApiPaths.Auth.Group}{ApiPaths.Auth.Logout}");

    private static readonly PathString GetMePath =
        new($"{ApiPaths.Base}/{ApiPaths.Users.Group}{ApiPaths.Users.Me}");

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
    {
        if (request.Method == HttpMethods.Get && request.Path == GetMePath)
            return true;
        return request.Method == HttpMethods.Post
            && (request.Path == ChangePasswordPath || request.Path == LogoutPath);
    }
}
