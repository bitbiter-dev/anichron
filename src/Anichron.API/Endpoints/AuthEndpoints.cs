using Anichron.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Anichron.API.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshTokenCookie = "refresh_token";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginWebAsync).AllowAnonymous();
        group.MapPost("/login/mobile", LoginMobileAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
        group.MapPost("/password-reset/request", PasswordResetRequest).AllowAnonymous();
        group.MapPost("/password-reset/confirm", PasswordResetConfirm).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest req, IAuthService auth, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(req.Username, req.Email, req.Password, ct);
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.UsernameTaken => Results.Conflict(new { error = "Username already taken." }),
                AuthError.EmailTaken => Results.Conflict(new { error = "Email already registered." }),
                AuthError.InvalidCredentials => Results.BadRequest(),
                AuthError.TokenInvalid => Results.BadRequest(),
                _ => Results.BadRequest(),
            };
        }

        return Results.Ok(new { result.Value!.AccessToken });
    }

    private static async Task<IResult> LoginWebAsync(
        LoginRequest req, IAuthService auth, HttpContext http, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req.UsernameOrEmail, req.Password, ct);
        if (!result.IsSuccess)
            return Results.Unauthorized();

        SetRefreshCookie(http, result.Value!.RefreshToken);
        return Results.Ok(new { result.Value.AccessToken });
    }

    private static async Task<IResult> LoginMobileAsync(
        LoginRequest req, IAuthService auth, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req.UsernameOrEmail, req.Password, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : Results.Unauthorized();
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http, IAuthService auth, [FromBody] RefreshRequest? req, CancellationToken ct)
    {
        var rawToken = http.Request.Cookies[RefreshTokenCookie] ?? req?.RefreshToken;
        if (rawToken is null)
            return Results.Unauthorized();

        var result = await auth.RefreshAsync(rawToken, ct);
        if (!result.IsSuccess)
            return Results.Unauthorized();

        var usedCookie = http.Request.Cookies.ContainsKey(RefreshTokenCookie);
        if (usedCookie)
        {
            SetRefreshCookie(http, result.Value!.RefreshToken);
            return Results.Ok(new { result.Value.AccessToken });
        }

        return Results.Ok(result.Value);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, IAuthService auth, [FromBody] RefreshRequest? req, CancellationToken ct)
    {
        var rawToken = http.Request.Cookies[RefreshTokenCookie] ?? req?.RefreshToken;
        if (rawToken is not null)
        {
            await auth.RevokeAsync(rawToken, ct);
            http.Response.Cookies.Delete(RefreshTokenCookie);
        }

        return Results.NoContent();
    }

    private static IResult PasswordResetRequest() => Results.StatusCode(StatusCodes.Status501NotImplemented);

    private static IResult PasswordResetConfirm() => Results.StatusCode(StatusCodes.Status501NotImplemented);

    private static void SetRefreshCookie(HttpContext http, string rawToken)
    {
        http.Response.Cookies.Append(RefreshTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });
    }
}

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string UsernameOrEmail, string Password);
public sealed record RefreshRequest(string RefreshToken);
