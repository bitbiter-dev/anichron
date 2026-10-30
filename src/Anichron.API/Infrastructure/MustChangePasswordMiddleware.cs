using Anichron.API.Endpoints;
using Anichron.API.Security;
using Microsoft.AspNetCore.Authorization;

namespace Anichron.API.Infrastructure;

internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private static readonly (string Method, PathString Path)[] exemptRoutes =
    [
        (HttpMethods.Get,  new(ApiPaths.Users.MePath)),
        (HttpMethods.Post, new(ApiPaths.Users.ChangePasswordPath)),
        (HttpMethods.Post, new(ApiPaths.Auth.LogoutPath)),
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(AppClaimTypes.MustChangePassword, "true")
            && !IsExemptRequest(context.Request))
        {
            await ApiProblems.Problem(
                    StatusCodes.Status403Forbidden, ErrorCodes.PasswordChangeRequired, AuthMessages.MustChangePassword)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsExemptRequest(HttpRequest request)
        => exemptRoutes.Any(e => e.Method == request.Method && e.Path == request.Path);
}
