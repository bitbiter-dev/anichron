using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using System.Diagnostics;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Endpoints;

public interface IAuthResponseMapper
{
    IResult GetRegistrationResult(AuthResult<AuthTokens> result, HttpContext http, PasswordPolicy passwordPolicy, UsernamePolicy usernamePolicy);
    IResult GetLoginResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie);
    IResult GetRefreshResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie);
    void ClearRefreshCookie(HttpContext http);
}

public sealed class AuthResponseMapper(AuthCookieSettings cookieSettings, IClock clock) : IAuthResponseMapper
{
    public IResult GetRegistrationResult(AuthResult<AuthTokens> result, HttpContext http, PasswordPolicy passwordPolicy, UsernamePolicy usernamePolicy)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.UsernameTaken => Results.Conflict(new { error = AuthMessages.UsernameTaken }),
                AuthError.EmailTaken => Results.Conflict(new { error = AuthMessages.EmailTaken }),
                AuthError.InvalidUsername => Results.UnprocessableEntity(new { error = usernamePolicy.InvalidFormatMessage }),
                AuthError.InvalidEmail => Results.UnprocessableEntity(new { error = AuthMessages.InvalidEmail }),
                AuthError.PasswordTooShort => Results.UnprocessableEntity(new { error = passwordPolicy.TooShortMessage }),
                AuthError.PasswordTooLong => Results.UnprocessableEntity(new { error = passwordPolicy.TooLongMessage }),
                AuthError.PasswordPwned => Results.UnprocessableEntity(new { error = AuthMessages.PasswordPwned }),
                // Not reachable from RegisterAsync; signals a logic bug if reached.
                AuthError.None or
                AuthError.InvalidCredentials or
                AuthError.TokenInvalid or
                AuthError.AccountDisabled or
                AuthError.AccountTemporarilyLocked
                    => throw new UnreachableException($"Unexpected AuthError in Register: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError in Register: {result.Error}"),
            };
        }

        SetRefreshCookie(http, result.Value!.RefreshToken);
        return Results.Ok(new { result.Value.AccessToken });
    }

    public IResult GetLoginResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.AccountTemporarilyLocked => LockedResult(http, result.RetryAfterSeconds!.Value),
                AuthError.AccountDisabled => Results.Json(
                    data: new { error = AuthMessages.AccountDisabled },
                    statusCode: StatusCodes.Status401Unauthorized),
                AuthError.InvalidCredentials => Results.Json(
                    data: new { error = AuthMessages.InvalidCredentials },
                    statusCode: StatusCodes.Status401Unauthorized),
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
                    => throw new UnreachableException($"Unexpected AuthError in Login: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError in Login: {result.Error}"),
            };
        }

        return BuildTokenResponse(result.Value!, http, setCookie);
    }

    public IResult GetRefreshResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.TokenInvalid => Results.Json(
                    data: new { error = AuthMessages.RefreshTokenInvalid },
                    statusCode: StatusCodes.Status401Unauthorized),
                AuthError.AccountDisabled => Results.Json(
                    data: new { error = AuthMessages.AccountDisabled },
                    statusCode: StatusCodes.Status401Unauthorized),
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
                    => throw new UnreachableException($"Unexpected AuthError in Refresh: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError in Refresh: {result.Error}"),
            };
        }

        return BuildTokenResponse(result.Value!, http, setCookie);
    }

    public void ClearRefreshCookie(HttpContext http)
        => http.Response.Cookies.Delete(AuthMessages.RefreshTokenCookieName);

    private IResult BuildTokenResponse(AuthTokens tokens, HttpContext http, bool setCookie)
    {
        if (!setCookie)
            return Results.Ok(new { tokens.AccessToken, tokens.RefreshToken });

        SetRefreshCookie(http, tokens.RefreshToken);
        return Results.Ok(new { tokens.AccessToken });
    }

    private static IResult LockedResult(HttpContext http, int secondsRemaining)
    {
        http.Response.Headers.RetryAfter = secondsRemaining.ToString(InvariantCulture);
        return Results.Json(
            new { error = AuthMessages.AccountTemporarilyLocked(secondsRemaining) },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    private void SetRefreshCookie(HttpContext http, string rawToken)
    {
        http.Response.Cookies.Append(AuthMessages.RefreshTokenCookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = cookieSettings.SameSite,
            Expires = clock.GetCurrentInstant().ToDateTimeOffset().AddDays(cookieSettings.RefreshTokenDays),
        });
    }
}
