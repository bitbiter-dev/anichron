using Anichron.API.Infrastructure;
using Anichron.API.Services;
using System.Security.Claims;

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
        group.MapGet(string.Empty, GetUsersAsync);
        group.MapGet("{userId:guid}", GetUserAsync);
        group.MapPatch("{userId:guid}", PatchUserAsync)
             .RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapDelete("{userId:guid}", DeleteUserAsync)
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

    internal static async Task<IResult> GetUsersAsync(
        IAdminUserService adminUsers,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var users = await adminUsers.GetAllAsync(ct);
        return mapper.GetAdminGetUsersResult(users);
    }

    internal static async Task<IResult> GetUserAsync(
        Guid userId,
        IAdminUserService adminUsers,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var user = await adminUsers.GetByIdAsync(userId, ct);
        return mapper.GetAdminGetUserResult(user);
    }

    internal static async Task<IResult> PatchUserAsync(
        Guid userId,
        PatchAdminUserRequest req,
        ClaimsPrincipal caller,
        IAdminUserService adminUsers,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        if (!Guid.TryParse(caller.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
            return Results.Unauthorized();

        var result = await adminUsers.UpdateAsync(callerId, userId, req.IsAdmin, req.IsDisabled, ct);
        return mapper.GetAdminPatchUserResult(result);
    }

    internal static async Task<IResult> DeleteUserAsync(
        Guid userId,
        ClaimsPrincipal caller,
        IAdminUserService adminUsers,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        if (!Guid.TryParse(caller.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
            return Results.Unauthorized();

        var result = await adminUsers.DeleteAsync(callerId, userId, ct);
        return mapper.GetAdminDeleteUserResult(result);
    }
}

public sealed record CreateAdminUserRequest(string Username, string Email);
public sealed record AdminCreatedUserResponse(Guid Id, string Username, string Email, string TemporaryPassword);
public sealed record AdminPasswordResetResponse(string TemporaryPassword);
public sealed record AdminUserResponse(Guid Id, string Username, string Email, bool IsAdmin, bool IsDisabled, int StorageConfigCount);
public sealed record PatchAdminUserRequest(bool? IsAdmin, bool? IsDisabled);
