using Anichron.API.Infrastructure;
using Anichron.API.Services;

namespace Anichron.API.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Users.Group).WithTags("Users");
        group.MapPost(string.Empty, CreateUserAsync)
             .RequireAuthorization(AuthPolicies.Admin);
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
}

public sealed record CreateAdminUserRequest(string Username, string Email);
public sealed record AdminCreatedUserResponse(Guid Id, string Username, string Email, string TemporaryPassword);
