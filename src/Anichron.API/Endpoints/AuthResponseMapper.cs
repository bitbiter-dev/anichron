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
    IResult GetAdminGetStorageConfigsResult(AuthResult<List<UserStorageConfig>> result);
    IResult GetAdminCreateStorageConfigResult(AuthResult<UserStorageConfig> result);
    IResult GetAdminDeleteStorageConfigResult(AuthResult result);
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
                AuthError.UsernameTaken => ApiProblems.FieldProblem(
                    StatusCodes.Status409Conflict, ErrorCodes.UsernameTaken, Fields.Username, AuthMessages.UsernameTaken),
                AuthError.EmailTaken => ApiProblems.FieldProblem(
                    StatusCodes.Status409Conflict, ErrorCodes.EmailTaken, Fields.Email, AuthMessages.EmailTaken),
                AuthError.InvalidUsername => ApiProblems.FieldProblem(
                    StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidUsername, Fields.Username, usernamePolicy.InvalidFormatMessage),
                AuthError.InvalidEmail => ApiProblems.FieldProblem(
                    StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidEmail, Fields.Email, AuthMessages.InvalidEmail),
                AuthError.InviteTokenInvalid => ApiProblems.Problem(
                    StatusCodes.Status422UnprocessableEntity, ErrorCodes.InviteTokenInvalid, AuthMessages.InviteTokenInvalid),
                _ => PasswordRejectedResult(result.Error, Fields.Password, passwordPolicy, "Register"),
            };
        }

        SetRefreshCookie(http, result.Value!.RefreshToken);
        return Results.Ok(new TokenResponse(result.Value.AccessToken));
    }

    public IResult GetLoginResult(AuthResult<AuthTokens> result, HttpContext http, bool setCookie)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.AccountTemporarilyLocked => LockedResult(http, result.RetryAfterSeconds!.Value),
                AuthError.AccountDisabled => AccountDisabledResult(),
                AuthError.InvalidCredentials => ApiProblems.Problem(
                    StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, AuthMessages.InvalidCredentials),
                _ => throw new UnreachableException($"Unexpected AuthError in Login: {result.Error}"),
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
                AuthError.TokenInvalid => ApiProblems.Problem(
                    StatusCodes.Status401Unauthorized, ErrorCodes.RefreshTokenInvalid, AuthMessages.RefreshTokenInvalid),
                AuthError.AccountDisabled => AccountDisabledResult(),
                AuthError.AccountTemporarilyLocked => LockedResult(http, result.RetryAfterSeconds!.Value),
                _ => throw new UnreachableException($"Unexpected AuthError in Refresh: {result.Error}"),
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
            AuthError.InvalidCredentials => ApiProblems.FieldProblem(
                StatusCodes.Status400BadRequest, ErrorCodes.InvalidCredentials, Fields.CurrentPassword, AuthMessages.InvalidCredentials),
            _ => PasswordRejectedResult(result.Error, Fields.NewPassword, passwordPolicy, "ChangePassword"),
        };
    }

    public IResult GetAdminCreateUserResult(AuthResult<AdminCreatedUser> result)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.InvalidUsername => ApiProblems.FieldProblem(
                    StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidUsername, Fields.Username, AuthMessages.InvalidUsername),
                AuthError.InvalidEmail => ApiProblems.FieldProblem(
                    StatusCodes.Status422UnprocessableEntity, ErrorCodes.InvalidEmail, Fields.Email, AuthMessages.InvalidEmail),
                AuthError.UsernameTaken => ApiProblems.FieldProblem(
                    StatusCodes.Status409Conflict, ErrorCodes.UsernameTaken, Fields.Username, AuthMessages.UsernameTaken),
                AuthError.EmailTaken => ApiProblems.FieldProblem(
                    StatusCodes.Status409Conflict, ErrorCodes.EmailTaken, Fields.Email, AuthMessages.EmailTaken),
                _ => throw new UnreachableException($"Unexpected AuthError in AdminCreateUser: {result.Error}"),
            };
        }

        var created = result.Value!;
        var location = ApiPaths.Users.UserLocation(created.Id);
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
            AuthError.CannotModifySelf => CannotModifySelfResult(),
            null => Results.Ok(ToAdminUserResponse(result.Value!)),
            _ => throw new UnreachableException($"Unexpected AuthError in AdminPatchUser: {result.Error}"),
        };

    public IResult GetAdminDeleteUserResult(AuthResult result)
        => result.Error switch
        {
            AuthError.UserNotFound => Results.NotFound(),
            AuthError.CannotModifySelf => CannotModifySelfResult(),
            null => Results.NoContent(),
            _ => throw new UnreachableException($"Unexpected AuthError in AdminDeleteUser: {result.Error}"),
        };

    public IResult GetAdminGetStorageConfigsResult(AuthResult<List<UserStorageConfig>> result)
        => result.Error switch
        {
            AuthError.UserNotFound => Results.NotFound(),
            null => Results.Ok(result.Value!.ConvertAll(ToAdminStorageConfigResponse)),
            _ => throw new UnreachableException($"Unexpected AuthError in AdminGetStorageConfigs: {result.Error}"),
        };

    public IResult GetAdminCreateStorageConfigResult(AuthResult<UserStorageConfig> result)
    {
        if (!result.IsSuccess)
        {
            return result.Error switch
            {
                AuthError.UserNotFound => Results.NotFound(),
                AuthError.PathInvalid => ApiProblems.FieldProblem(
                    StatusCodes.Status422UnprocessableEntity, ErrorCodes.PathInvalid, Fields.RootPath, AuthMessages.PathInvalid),
                AuthError.PathAlreadyAssigned => ApiProblems.FieldProblem(
                    StatusCodes.Status409Conflict, ErrorCodes.PathAlreadyAssigned, Fields.RootPath, AuthMessages.PathAlreadyAssigned),
                AuthError.PathOverlaps => ApiProblems.FieldProblem(
                    StatusCodes.Status409Conflict, ErrorCodes.PathOverlaps, Fields.RootPath, AuthMessages.PathOverlaps),
                _ => throw new UnreachableException($"Unexpected AuthError in AdminCreateStorageConfig: {result.Error}"),
            };
        }

        var config = result.Value!;
        var location = ApiPaths.Users.StorageConfigLocation(config.UserId, config.Id);
        return Results.Created(location, ToAdminStorageConfigResponse(config));
    }

    public IResult GetAdminDeleteStorageConfigResult(AuthResult result)
        => result.Error switch
        {
            AuthError.StorageConfigNotFound => Results.NotFound(),
            null => Results.NoContent(),
            _ => throw new UnreachableException($"Unexpected AuthError in AdminDeleteStorageConfig: {result.Error}"),
        };

    public void ClearRefreshCookie(HttpContext http)
        => http.Response.Cookies.Delete(AuthMessages.RefreshTokenCookieName);

    private static class Fields
    {
        internal const string Username = "username";
        internal const string Email = "email";
        internal const string Password = "password";
        internal const string CurrentPassword = "currentPassword";
        internal const string NewPassword = "newPassword";
        internal const string RootPath = "rootPath";
    }

    private static AdminUserResponse ToAdminUserResponse(User user)
        => new(user.Id, user.Username, user.Email, user.IsAdmin, user.IsDisabled, user.StorageConfigs.Count);

    private static AdminStorageConfigResponse ToAdminStorageConfigResponse(UserStorageConfig config)
        => new(config.Id, config.UserId, config.RootPath, config.IsActive);

    private IResult BuildTokenResponse(AuthTokens tokens, HttpContext http, bool setCookie)
    {
        if (!setCookie)
            return Results.Ok(new TokenResponse(tokens.AccessToken, tokens.RefreshToken));

        SetRefreshCookie(http, tokens.RefreshToken);
        return Results.Ok(new TokenResponse(tokens.AccessToken));
    }

    private static IResult LockedResult(HttpContext http, int secondsRemaining)
    {
        http.Response.Headers.RetryAfter = secondsRemaining.ToString(InvariantCulture);
        return ApiProblems.Problem(
            StatusCodes.Status429TooManyRequests, ErrorCodes.AccountLocked, AuthMessages.AccountTemporarilyLocked(secondsRemaining));
    }

    private static IResult PasswordRejectedResult(AuthError? error, string field, PasswordPolicy passwordPolicy, string operation)
        => error switch
        {
            AuthError.PasswordTooShort => ApiProblems.FieldProblem(
                StatusCodes.Status422UnprocessableEntity, ErrorCodes.PasswordTooShort, field, passwordPolicy.TooShortMessage),
            AuthError.PasswordTooLong => ApiProblems.FieldProblem(
                StatusCodes.Status422UnprocessableEntity, ErrorCodes.PasswordTooLong, field, passwordPolicy.TooLongMessage),
            AuthError.PasswordPwned => ApiProblems.FieldProblem(
                StatusCodes.Status422UnprocessableEntity, ErrorCodes.PasswordPwned, field, AuthMessages.PasswordPwned),
            _ => throw new UnreachableException($"Unexpected AuthError in {operation}: {error}"),
        };

    private static IResult CannotModifySelfResult()
        => ApiProblems.Problem(StatusCodes.Status403Forbidden, ErrorCodes.CannotModifySelf, AuthMessages.CannotModifySelf);

    private static IResult AccountDisabledResult()
        => ApiProblems.Problem(StatusCodes.Status401Unauthorized, ErrorCodes.AccountDisabled, AuthMessages.AccountDisabled);

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
