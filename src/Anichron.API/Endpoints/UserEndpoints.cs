using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Users.Group).WithTags("Users");
        group.MapGet(ApiPaths.Users.Me, GetMeAsync)
             .RequireUserAllowedWhileMustChangePassword()
             .Produces<UserProfileResponse>()
             .Produces(StatusCodes.Status404NotFound);
        group.MapPost(ApiPaths.Users.ChangePassword, ChangePasswordAsync)
             .RequireUserAllowedWhileMustChangePassword()
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces<SessionResponse>()
             .ProducesValidationProblem()
             .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
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
            : Results.Ok(UserProfileResponse.From(found));
    }

    internal static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest req,
        ClaimsPrincipal user,
        HttpContext http,
        IAuthService auth,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var result = await auth.ChangePasswordAsync(userId, req.CurrentPassword, req.NewPassword, ct);
        return mapper.GetChangePasswordResult(result, http, AuthEndpoints.CameWithRefreshCookie(http));
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UserProfileResponse(Guid Id, string Username, string Email, bool IsAdmin, bool MustChangePassword)
{
    internal static UserProfileResponse From(User user)
        => new(user.Id, user.Username, user.Email, user.IsAdmin, user.MustChangePassword);
}
