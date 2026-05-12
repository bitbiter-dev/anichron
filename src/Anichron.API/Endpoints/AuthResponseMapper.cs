using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using System.Diagnostics;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Endpoints;

public interface IAuthResponseMapper
{
    IResult GetRegistrationResult(AuthResult<AuthTokens> result, HttpContext http, PasswordPolicy passwordPolicy, UsernamePolicy usernamePolicy);
    IResult GetLoginResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie);
    IResult GetRefreshResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie);
    IResult GetChangePasswordResult(AuthResult result, PasswordPolicy passwordPolicy);
    IResult GetAdminCreateUserResult(AuthResult<AdminCreatedUser> result);
    IResult GetAdminResetPasswordResult(AdminUserPasswordReset? result);
    IResult GetAdminGetUsersResult(List<User> users);
    IResult GetAdminGetUserResult(User? user);
    IResult GetAdminPatchUserResult(AuthResult<User> result);
    IResult GetAdminDeleteUserResult(AuthResult result);
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
                AuthError.InviteTokenInvalid => Results.UnprocessableEntity(new { error = AuthMessages.InviteTokenInvalid }),
                // Not reachable from RegisterAsync; signals a logic bug if reached.
                AuthError.None or
                AuthError.InvalidCredentials or
                AuthError.TokenInvalid or
                AuthError.AccountDisabled or
                AuthError.AccountTemporarilyLocked or
                AuthError.CannotModifySelf or
                AuthError.UserNotFound
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
                AuthError.PasswordPwned or
                AuthError.InviteTokenInvalid or
                AuthError.CannotModifySelf or
                AuthError.UserNotFound
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
                AuthError.PasswordPwned or
                AuthError.InviteTokenInvalid or
                AuthError.CannotModifySelf or
                AuthError.UserNotFound
                    => throw new UnreachableException($"Unexpected AuthError in Refresh: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError in Refresh: {result.Error}"),
            };
        }

        return BuildTokenResponse(result.Value!, http, setCookie);
    }

    public IResult GetChangePasswordResult(AuthResult result, PasswordPolicy passwordPolicy)
    {
        if (result.IsSuccess)
            return Results.NoContent();

        return result.Error switch
        {
            AuthError.InvalidCredentials => Results.Json(
                data: new { error = AuthMessages.InvalidCredentials },
                statusCode: StatusCodes.Status400BadRequest),
            AuthError.PasswordTooShort => Results.Json(
                data: new { error = passwordPolicy.TooShortMessage },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            AuthError.PasswordTooLong => Results.Json(
                data: new { error = passwordPolicy.TooLongMessage },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            AuthError.PasswordPwned => Results.Json(
                data: new { error = AuthMessages.PasswordPwned },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            // Not reachable from ChangePasswordAsync; signals a logic bug if reached.
            AuthError.None or
            AuthError.UsernameTaken or
            AuthError.EmailTaken or
            AuthError.TokenInvalid or
            AuthError.InvalidUsername or
            AuthError.InvalidEmail or
            AuthError.AccountDisabled or
            AuthError.AccountTemporarilyLocked or
            AuthError.InviteTokenInvalid or
            AuthError.CannotModifySelf or
            AuthError.UserNotFound
                => throw new UnreachableException($"Unexpected AuthError in ChangePassword: {result.Error}"),
            _ => throw new UnreachableException($"Unhandled AuthError in ChangePassword: {result.Error}"),
        };
    }

    public IResult GetAdminCreateUserResult(AuthResult<AdminCreatedUser> result)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.UsernameTaken => Results.Conflict(new { error = AuthMessages.UsernameTaken }),
                AuthError.EmailTaken => Results.Conflict(new { error = AuthMessages.EmailTaken }),
                // Not reachable from AdminCreateUserAsync; signals a logic bug if reached.
                AuthError.None or
                AuthError.InvalidCredentials or
                AuthError.TokenInvalid or
                AuthError.InvalidUsername or
                AuthError.InvalidEmail or
                AuthError.PasswordTooShort or
                AuthError.PasswordTooLong or
                AuthError.PasswordPwned or
                AuthError.AccountDisabled or
                AuthError.AccountTemporarilyLocked or
                AuthError.InviteTokenInvalid or
                AuthError.CannotModifySelf or
                AuthError.UserNotFound
                    => throw new UnreachableException($"Unexpected AuthError in AdminCreateUser: {result.Error}"),
                _ => throw new UnreachableException($"Unhandled AuthError in AdminCreateUser: {result.Error}"),
            };
        }

        var created = result.Value!;
        var location = $"{ApiPaths.Base}/{ApiPaths.Users.Group}/{created.Id}";
        return Results.Created(location, new AdminCreatedUserResponse(
            created.Id, created.Username, created.Email, created.TemporaryPassword));
    }

    public IResult GetAdminResetPasswordResult(AdminUserPasswordReset? result)
        => result is null
            ? Results.NotFound()
            : Results.Ok(new AdminPasswordResetResponse(result.TemporaryPassword));

    public IResult GetAdminGetUsersResult(List<User> users)
        => Results.Ok(users.ConvertAll(ToAdminUserResponse));

    public IResult GetAdminGetUserResult(User? user)
        => user is null ? Results.NotFound() : Results.Ok(ToAdminUserResponse(user));

    public IResult GetAdminPatchUserResult(AuthResult<User> result)
        => result.Error switch
        {
            AuthError.UserNotFound => Results.NotFound(),
            AuthError.CannotModifySelf => Results.BadRequest(new { error = AuthMessages.CannotModifySelf }),
            null => Results.Ok(ToAdminUserResponse(result.Value!)),
            AuthError.None or
            AuthError.UsernameTaken or
            AuthError.EmailTaken or
            AuthError.InvalidCredentials or
            AuthError.TokenInvalid or
            AuthError.InvalidUsername or
            AuthError.InvalidEmail or
            AuthError.PasswordTooShort or
            AuthError.PasswordTooLong or
            AuthError.PasswordPwned or
            AuthError.AccountDisabled or
            AuthError.AccountTemporarilyLocked or
            AuthError.InviteTokenInvalid
                => throw new UnreachableException($"Unexpected AuthError in AdminPatchUser: {result.Error}"),
            _ => throw new UnreachableException($"Unhandled AuthError in AdminPatchUser: {result.Error}"),
        };

    public IResult GetAdminDeleteUserResult(AuthResult result)
        => result.Error switch
        {
            AuthError.UserNotFound => Results.NotFound(),
            AuthError.CannotModifySelf => Results.BadRequest(new { error = AuthMessages.CannotModifySelf }),
            null => Results.NoContent(),
            AuthError.None or
            AuthError.UsernameTaken or
            AuthError.EmailTaken or
            AuthError.InvalidCredentials or
            AuthError.TokenInvalid or
            AuthError.InvalidUsername or
            AuthError.InvalidEmail or
            AuthError.PasswordTooShort or
            AuthError.PasswordTooLong or
            AuthError.PasswordPwned or
            AuthError.AccountDisabled or
            AuthError.AccountTemporarilyLocked or
            AuthError.InviteTokenInvalid
                => throw new UnreachableException($"Unexpected AuthError in AdminDeleteUser: {result.Error}"),
            _ => throw new UnreachableException($"Unhandled AuthError in AdminDeleteUser: {result.Error}"),
        };

    public void ClearRefreshCookie(HttpContext http)
        => http.Response.Cookies.Delete(AuthMessages.RefreshTokenCookieName);

    private static AdminUserResponse ToAdminUserResponse(User user)
        => new(user.Id, user.Username, user.Email, user.IsAdmin, user.IsDisabled, user.StorageConfigs.Count);

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
