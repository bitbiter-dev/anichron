using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Anichron.API.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Auth.Group).WithTags("Auth");

        group.MapPost(ApiPaths.Auth.Register, RegisterAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost(ApiPaths.Auth.Login, LoginWebAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost(ApiPaths.Auth.LoginMobile, LoginMobileAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost(ApiPaths.Auth.Refresh, RefreshAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Refresh);
        group.MapPost(ApiPaths.Auth.Logout, LogoutAsync).RequireAuthorization();
        group.MapPost(ApiPaths.Auth.PasswordResetRequest, PasswordResetRequest).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost(ApiPaths.Auth.PasswordResetConfirm, PasswordResetConfirm).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);

        return app;
    }

    internal static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IAuthService auth,
        IAuthResponseMapper mapper,
        HttpContext http,
        IOptions<PasswordPolicy> passwordPolicy,
        IOptions<UsernamePolicy> usernamePolicy,
        CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request.Username, request.Email, request.Password, request.InviteToken, ct);
        return mapper.GetRegistrationResult(result, http, passwordPolicy.Value, usernamePolicy.Value);
    }

    internal static Task<IResult> LoginWebAsync(
        LoginRequest request, IAuthService auth, IAuthResponseMapper mapper, HttpContext http, CancellationToken ct)
        => HandleLoginAsync(request, auth, mapper, http, setCookie: true, ct);

    internal static Task<IResult> LoginMobileAsync(
        LoginRequest request, IAuthService auth, IAuthResponseMapper mapper, HttpContext http, CancellationToken ct)
        => HandleLoginAsync(request, auth, mapper, http, setCookie: false, ct);

    private static async Task<IResult> HandleLoginAsync(
        LoginRequest request, IAuthService auth, IAuthResponseMapper mapper, HttpContext http, bool setCookie, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request.UsernameOrEmail, request.Password, ct);
        return mapper.GetLoginResult(result, http, setCookie);
    }

    internal static async Task<IResult> RefreshAsync(
        HttpContext http,
        IAuthService auth,
        IAuthResponseMapper mapper,
        // [FromBody] is explicit here because the request is nullable —
        // web clients send no body (token arrives via cookie), mobile clients send it in the body.
        [FromBody] RefreshRequest? request,
        CancellationToken ct)
    {
        var rawToken = http.Request.Cookies[AuthMessages.RefreshTokenCookieName] ?? request?.RefreshToken;
        if (rawToken is null)
        {
            return Results.Json(
                data: new { error = AuthMessages.RefreshTokenRequired },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await auth.RefreshAsync(rawToken, ct);
        var setCookie = http.Request.Cookies.ContainsKey(AuthMessages.RefreshTokenCookieName);
        return mapper.GetRefreshResult(result, http, setCookie);
    }

    internal static async Task<IResult> LogoutAsync(
        HttpContext http, IAuthService auth, IAuthResponseMapper mapper, [FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var rawToken = http.Request.Cookies[AuthMessages.RefreshTokenCookieName] ?? request?.RefreshToken;
        mapper.ClearRefreshCookie(http);
        if (rawToken is not null)
            await auth.RevokeAsync(rawToken, ct);
        return Results.NoContent();
    }

    // Not yet implemented — planned for Epic 8 (email notifications with deep links).
    private static IResult PasswordResetRequest() => Results.StatusCode(StatusCodes.Status501NotImplemented);

    private static IResult PasswordResetConfirm() => Results.StatusCode(StatusCodes.Status501NotImplemented);
}

public sealed record RegisterRequest(string Username, string Email, string Password, string InviteToken);
public sealed record LoginRequest(string UsernameOrEmail, string Password);
public sealed record RefreshRequest(string RefreshToken);
