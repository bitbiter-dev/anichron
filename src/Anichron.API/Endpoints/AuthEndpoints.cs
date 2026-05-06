using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshTokenCookie = "refresh_token";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost("/login", LoginWebAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost("/login/mobile", LoginMobileAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Refresh);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapPost("/password-reset/request", PasswordResetRequest).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);
        group.MapPost("/password-reset/confirm", PasswordResetConfirm).AllowAnonymous().RequireRateLimiting(AuthRateLimitPolicies.Sensitive);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest req,
        IAuthService auth,
        HttpContext http,
        IOptions<PasswordPolicy> passwordPolicy,
        IOptions<UsernamePolicy> usernamePolicy,
        AuthCookieSettings cookieSettings,
        IClock clock,
        CancellationToken ct)
    {
        var result = await auth.RegisterAsync(req.Username, req.Email, req.Password, ct);
        if (result.IsSuccess)
        {
            SetRefreshCookie(http, result.Value!.RefreshToken, cookieSettings, clock);
            return Results.Ok(new { result.Value.AccessToken });
        }

        var pp = passwordPolicy.Value;
        var up = usernamePolicy.Value;
        return result.Error switch
        {
            AuthError.UsernameTaken => Results.Conflict(new { error = AuthMessages.UsernameTaken }),
            AuthError.EmailTaken => Results.Conflict(new { error = AuthMessages.EmailTaken }),
            AuthError.InvalidUsername => Results.UnprocessableEntity(new { error = up.InvalidFormatMessage }),
            AuthError.InvalidEmail => Results.UnprocessableEntity(new { error = AuthMessages.InvalidEmail }),
            AuthError.PasswordTooShort => Results.UnprocessableEntity(new { error = pp.TooShortMessage }),
            AuthError.PasswordTooLong => Results.UnprocessableEntity(new { error = pp.TooLongMessage }),
            AuthError.PasswordPwned => Results.UnprocessableEntity(new { error = AuthMessages.PasswordPwned }),
            // Not reachable from RegisterAsync; signals a logic bug if reached.
            AuthError.None or AuthError.InvalidCredentials or AuthError.TokenInvalid
                or AuthError.AccountDisabled or AuthError.AccountTemporarilyLocked
                => throw new UnreachableException($"Unexpected AuthError in RegisterAsync: {result.Error}"),
            _ => throw new UnreachableException($"Unhandled AuthError: {result.Error}"),
        };
    }

    private static Task<IResult> LoginWebAsync(
        LoginRequest req,
        IAuthService auth,
        HttpContext http,
        AuthCookieSettings cookieSettings,
        IClock clock,
        CancellationToken ct)
        => HandleLoginAsync(req, auth, tokens =>
        {
            SetRefreshCookie(http, tokens.RefreshToken, cookieSettings, clock);
            return Results.Ok(new { tokens.AccessToken });
        }, http, ct);

    private static Task<IResult> LoginMobileAsync(
        LoginRequest req, IAuthService auth, HttpContext http, CancellationToken ct)
        => HandleLoginAsync(req, auth, tokens =>
            Results.Ok(new { tokens.AccessToken, tokens.RefreshToken }), http, ct);

    private static async Task<IResult> HandleLoginAsync(
        LoginRequest req,
        IAuthService auth,
        Func<AuthTokens, IResult> onSuccess,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await auth.LoginAsync(req.UsernameOrEmail, req.Password, ct);
        return result.IsSuccess
            ? onSuccess(result.Value!)
            : result.Error switch
            {
                AuthError.AccountTemporarilyLocked => LockedResult(http, result.RetryAfterSeconds!.Value),
                AuthError.AccountDisabled => Results.Json(
                    new { error = AuthMessages.AccountDisabled }, statusCode: StatusCodes.Status401Unauthorized),
                AuthError.InvalidCredentials => Results.Json(
                    new { error = AuthMessages.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized),
                // Not reachable from LoginAsync; signals a logic bug if reached.
                AuthError.None or
                AuthError.UsernameTaken or
                AuthError.EmailTaken or
                AuthError.TokenInvalid or
                AuthError.InvalidUsername or
                AuthError.InvalidEmail or
                AuthError.PasswordTooShort or
                AuthError.PasswordTooLong or
                AuthError.PasswordPwned
                    => throw new UnreachableException($"Unexpected AuthError: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError: {result.Error}"),
            };
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        IAuthService auth,
        AuthCookieSettings cookieSettings,
        IClock clock,
        // [FromBody] is explicit here because the request is nullable —
        // web clients send no body (token arrives via cookie), mobile clients send it in the body.
        [FromBody] RefreshRequest? req,
        CancellationToken ct)
    {
        var rawToken = http.Request.Cookies[RefreshTokenCookie] ?? req?.RefreshToken;
        if (rawToken is null)
        {
            return Results.Json(
                data: new { error = AuthMessages.RefreshTokenRequired },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await auth.RefreshAsync(rawToken, ct);
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.TokenInvalid => Results.Json(
                    new { error = AuthMessages.RefreshTokenInvalid }, statusCode: 401),
                AuthError.AccountDisabled => Results.Json(
                    new { error = AuthMessages.AccountDisabled }, statusCode: 401),
                AuthError.AccountTemporarilyLocked => LockedResult(http, result.RetryAfterSeconds!.Value),
                // Not reachable from RefreshAsync; signals a logic bug if reached.
                AuthError.None or
                AuthError.UsernameTaken or
                AuthError.EmailTaken or
                AuthError.InvalidCredentials or
                AuthError.InvalidUsername or
                AuthError.InvalidEmail or
                AuthError.PasswordTooShort or
                AuthError.PasswordTooLong or
                AuthError.PasswordPwned
                    => throw new UnreachableException($"Unhandled AuthError: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError: {result.Error}")
            };
        }

        var usedCookie = http.Request.Cookies.ContainsKey(RefreshTokenCookie);
        if (!usedCookie)
            return Results.Ok(new { result.Value!.AccessToken, result.Value.RefreshToken });

        SetRefreshCookie(http, result.Value!.RefreshToken, cookieSettings, clock);
        return Results.Ok(new { result.Value.AccessToken });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, IAuthService auth, [FromBody] RefreshRequest? req, CancellationToken ct)
    {
        var rawToken = http.Request.Cookies[RefreshTokenCookie] ?? req?.RefreshToken;
        http.Response.Cookies.Delete(RefreshTokenCookie);
        if (rawToken is null)
            return Results.NoContent();
        await auth.RevokeAsync(rawToken, ct);

        return Results.NoContent();
    }

    private static IResult PasswordResetRequest() => Results.StatusCode(StatusCodes.Status501NotImplemented);

    private static IResult PasswordResetConfirm() => Results.StatusCode(StatusCodes.Status501NotImplemented);

    private static IResult LockedResult(HttpContext http, int secondsRemaining)
    {
        http.Response.Headers.RetryAfter = secondsRemaining.ToString(InvariantCulture);
        return Results.Json(
            new { error = AuthMessages.AccountTemporarilyLocked(secondsRemaining) },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static void SetRefreshCookie(HttpContext http, string rawToken, AuthCookieSettings cookieSettings, IClock clock)
    {
        http.Response.Cookies.Append(RefreshTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = cookieSettings.SameSite,
            Expires = clock.GetCurrentInstant().ToDateTimeOffset().AddDays(cookieSettings.RefreshTokenDays),
        });
    }
}

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string UsernameOrEmail, string Password);
public sealed record RefreshRequest(string RefreshToken);
