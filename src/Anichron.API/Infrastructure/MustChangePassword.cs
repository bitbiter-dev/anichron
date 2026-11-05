using Anichron.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Anichron.API.Infrastructure;

/// <summary>Refuses a user whose password must change, unless the endpoint carries <see cref="AllowedWhileMustChangePasswordAttribute"/>.</summary>
internal sealed class MustChangePasswordRequirement
    : AuthorizationHandler<MustChangePasswordRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MustChangePasswordRequirement requirement)
    {
        if (!context.User.HasClaim(AppClaimTypes.MustChangePassword, "true") || IsAllowed(context.Resource as HttpContext))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static bool IsAllowed(HttpContext? http)
        => http?.GetEndpoint()?.Metadata.GetMetadata<AllowedWhileMustChangePasswordAttribute>() is not null;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
internal sealed class AllowedWhileMustChangePasswordAttribute : Attribute;

/// <summary>Answers a refusal by <see cref="MustChangePasswordRequirement"/> with its own Problem Details.</summary>
internal sealed class MustChangePasswordResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        => authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<MustChangePasswordRequirement>().Any() == true
            ? ApiProblems.Problem(StatusCodes.Status403Forbidden, ErrorCodes.PasswordChangeRequired, AuthMessages.MustChangePassword)
                .ExecuteAsync(context)
            : defaultHandler.HandleAsync(next, context, policy, authorizeResult);
}
