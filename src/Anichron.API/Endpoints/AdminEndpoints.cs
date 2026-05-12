using Anichron.API.Infrastructure;
using Anichron.API.Services;

namespace Anichron.API.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Users.Group).WithTags("Users")
                       .RequireAuthorization(AuthPolicies.Admin);
        group.MapPost(string.Empty, CreateUserAsync)
             .RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost("{userId:guid}/password-reset", ResetUserPasswordAsync)
             .RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        return app;
    }

    internal static async Task<IResult> CreateUserAsync(
        CreateAdminUserRequest req,
        IAuthService auth,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var result = await auth.AdminCreateUserAsync(req.Username, req.Email, ct);
        return mapper.GetAdminCreateUserResult(result);
    }

    internal static async Task<IResult> ResetUserPasswordAsync(
        Guid userId,
        IAdminResetService adminReset,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var result = await adminReset.ResetUserPasswordAsync(userId, ct);
        return mapper.GetAdminResetPasswordResult(result);
    }
}

public sealed record CreateAdminUserRequest(string Username, string Email);
public sealed record AdminCreatedUserResponse(Guid Id, string Username, string Email, string TemporaryPassword);
public sealed record AdminPasswordResetResponse(string TemporaryPassword);
