using Anichron.API.Infrastructure;
using Anichron.API.Services;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Users.Group).WithTags("Users")
                       .RequireAdmin();
        group.MapPost(string.Empty, CreateUserAsync)
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces<AdminCreatedUserResponse>(StatusCodes.Status201Created)
             .ProducesValidationProblem(StatusCodes.Status409Conflict)
             .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost(ApiPaths.Users.PasswordReset, ResetUserPasswordAsync)
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces<AdminPasswordResetResponse>()
             .Produces(StatusCodes.Status404NotFound);
        group.MapGet(string.Empty, GetUsersAsync)
             .Produces<List<AdminUserResponse>>();
        group.MapGet(ApiPaths.Users.ById, GetUserAsync)
             .Produces<AdminUserResponse>()
             .Produces(StatusCodes.Status404NotFound);
        group.MapPatch(ApiPaths.Users.ById, PatchUserAsync)
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces<AdminUserResponse>()
             .Produces(StatusCodes.Status404NotFound);
        group.MapDelete(ApiPaths.Users.ById, DeleteUserAsync)
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces(StatusCodes.Status204NoContent)
             .Produces(StatusCodes.Status404NotFound);
        group.MapGet(ApiPaths.Users.UserStorageConfigs, GetStorageConfigsAsync)
             .Produces<List<AdminStorageConfigResponse>>()
             .Produces(StatusCodes.Status404NotFound);
        group.MapPost(ApiPaths.Users.UserStorageConfigs, CreateStorageConfigAsync)
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces<AdminStorageConfigResponse>(StatusCodes.Status201Created)
             .Produces(StatusCodes.Status404NotFound)
             .ProducesValidationProblem(StatusCodes.Status409Conflict)
             .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapDelete(ApiPaths.Users.UserStorageConfigById, DeleteStorageConfigAsync)
             .RequireRateLimit(AuthRateLimitPolicies.Sensitive)
             .Produces(StatusCodes.Status204NoContent)
             .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    internal static async Task<IResult> CreateUserAsync(
        CreateAdminUserRequest request,
        IAuthService auth,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var result = await auth.AdminCreateUserAsync(request.Username, request.Email, ct);
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
        PatchAdminUserRequest request,
        ClaimsPrincipal caller,
        IAdminUserService adminUsers,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        if (!Guid.TryParse(caller.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
            return Results.Unauthorized();

        var result = await adminUsers.UpdateAsync(callerId, userId, request.IsAdmin, request.IsDisabled, ct);
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

    internal static async Task<IResult> GetStorageConfigsAsync(
        Guid userId,
        IAdminStorageConfigService adminStorageConfigs,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var result = await adminStorageConfigs.GetByUserIdAsync(userId, ct);
        return mapper.GetAdminGetStorageConfigsResult(result);
    }

    internal static async Task<IResult> CreateStorageConfigAsync(
        Guid userId,
        CreateStorageConfigRequest request,
        IAdminStorageConfigService adminStorageConfigs,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var result = await adminStorageConfigs.AddAsync(userId, request.RootPath, ct);
        return mapper.GetAdminCreateStorageConfigResult(result);
    }

    internal static async Task<IResult> DeleteStorageConfigAsync(
        Guid userId,
        Guid configId,
        IAdminStorageConfigService adminStorageConfigs,
        IAuthResponseMapper mapper,
        CancellationToken ct)
    {
        var result = await adminStorageConfigs.DeleteAsync(userId, configId, ct);
        return mapper.GetAdminDeleteStorageConfigResult(result);
    }
}

public sealed record CreateAdminUserRequest(string Username, string Email);
public sealed record AdminCreatedUserResponse(Guid Id, string Username, string Email, string TemporaryPassword);
public sealed record AdminPasswordResetResponse(string TemporaryPassword);
public sealed record AdminUserResponse(Guid Id, string Username, string Email, bool IsAdmin, bool IsDisabled, int StorageConfigCount);
public sealed record PatchAdminUserRequest(bool? IsAdmin, bool? IsDisabled);
public sealed record CreateStorageConfigRequest(string RootPath);
public sealed record AdminStorageConfigResponse(Guid Id, Guid UserId, string RootPath, bool IsActive);
