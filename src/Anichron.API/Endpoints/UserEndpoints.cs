using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data.Repository;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Users.Group).WithTags("Users");
        group.MapGet(ApiPaths.Users.Me, GetMeAsync)
             .RequireAuthorization();
        group.MapPost(ApiPaths.Users.ChangePassword, ChangePasswordAsync)
             .RequireAuthorization()
             .RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        return app;
    }

    internal static async Task<IResult> GetMeAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var found = await users.FindByIdAsync(userId, ct);
        return found is null
            ? Results.NotFound()
            : Results.Ok(new UserProfileResponse(found.Id, found.Username, found.Email, found.IsAdmin, found.MustChangePassword));
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
public sealed record UserProfileResponse(Guid Id, string Username, string Email, bool IsAdmin, bool MustChangePassword);
