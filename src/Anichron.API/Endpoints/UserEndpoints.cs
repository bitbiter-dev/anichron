using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("users").WithTags("Users");
        group.MapPost("/me/password", ChangePasswordAsync)
             .RequireAuthorization()
             .RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        return app;
    }

    internal static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest req,
        ClaimsPrincipal user,
        IAuthService auth,
        IAuthResponseMapper mapper,
        IOptions<PasswordPolicy> passwordPolicyOptions,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var result = await auth.ChangePasswordAsync(userId, req.CurrentPassword, req.NewPassword, ct);
        return mapper.GetChangePasswordResult(result, passwordPolicyOptions.Value);
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
