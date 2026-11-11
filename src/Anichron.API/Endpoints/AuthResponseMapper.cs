using Anichron.API.Infrastructure;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;

namespace Anichron.API.Endpoints;

public interface IAuthResponseMapper
{
    IResult GetRegistrationResult(ServiceResult<AuthTokens, IRegisterError> result, HttpContext http);
    IResult GetLoginResult(ServiceResult<AuthTokens, ILoginError> result, HttpContext http, bool setCookie);
    IResult GetRefreshResult(ServiceResult<AuthTokens, IRefreshError> result, HttpContext http, bool setCookie);
    IResult GetChangePasswordResult(ServiceResult<AuthTokens, IChangePasswordError> result, HttpContext http, bool setCookie);
    IResult GetAdminCreateUserResult(ServiceResult<AdminCreatedUser, IAdminCreateUserError> result);
    IResult GetAdminResetPasswordResult(AdminUserPasswordReset? result);
    IResult GetAdminGetUsersResult(List<User> users);
    IResult GetAdminGetUserResult(User? user);
    IResult GetAdminPatchUserResult(ServiceResult<User, IUpdateUserError> result);
    IResult GetAdminDeleteUserResult(IDeleteUserError? error);
    IResult GetAdminGetStorageConfigsResult(List<UserStorageConfig>? configs);
    IResult GetAdminCreateStorageConfigResult(ServiceResult<UserStorageConfig, IAddStorageConfigError> result);
    IResult GetAdminDeleteStorageConfigResult(bool deleted);
    void ClearSessionCookies(HttpContext http);
}

public sealed class AuthResponseMapper(
    AuthCookieSettings cookieSettings,
    IClock clock,
    IOptions<UsernamePolicy> usernamePolicy,
    IOptions<PasswordPolicy> passwordPolicy) : IAuthResponseMapper
{
    private readonly AuthProblems problems = new(usernamePolicy.Value, passwordPolicy.Value, AuthProblems.Fields.Password);
    private readonly AuthProblems changePasswordProblems = new(usernamePolicy.Value, passwordPolicy.Value, AuthProblems.Fields.NewPassword);

    public IResult GetRegistrationResult(ServiceResult<AuthTokens, IRegisterError> result, HttpContext http)
        => result.Match(tokens => BuildSessionResponse(tokens, http, setCookie: true), error => error.Accept(problems));

    public IResult GetLoginResult(ServiceResult<AuthTokens, ILoginError> result, HttpContext http, bool setCookie)
        => result.Match(tokens => BuildSessionResponse(tokens, http, setCookie), error => error.Accept(problems));

    public IResult GetRefreshResult(ServiceResult<AuthTokens, IRefreshError> result, HttpContext http, bool setCookie)
        => result.Match(tokens => BuildSessionResponse(tokens, http, setCookie), error => error.Accept(problems));

    public IResult GetChangePasswordResult(ServiceResult<AuthTokens, IChangePasswordError> result, HttpContext http, bool setCookie)
        => result.Match(tokens => BuildSessionResponse(tokens, http, setCookie), error => error.Accept(changePasswordProblems));

    public IResult GetAdminCreateUserResult(ServiceResult<AdminCreatedUser, IAdminCreateUserError> result)
        => result.Match(
            created => Results.Created(
                ApiPaths.Users.UserLocation(created.Id),
                new AdminCreatedUserResponse(created.Id, created.Username, created.Email, created.TemporaryPassword)),
            error => error.Accept(problems));

    public IResult GetAdminResetPasswordResult(AdminUserPasswordReset? result)
        => result is null
            ? Results.NotFound()
            : Results.Ok(new AdminPasswordResetResponse(result.TemporaryPassword));

    public IResult GetAdminGetUsersResult(List<User> users)
        => Results.Ok(users.ConvertAll(ToAdminUserResponse));

    public IResult GetAdminGetUserResult(User? user)
        => user is null ? Results.NotFound() : Results.Ok(ToAdminUserResponse(user));

    public IResult GetAdminPatchUserResult(ServiceResult<User, IUpdateUserError> result)
        => result.Match(user => Results.Ok(ToAdminUserResponse(user)), error => error.Accept(problems));

    public IResult GetAdminDeleteUserResult(IDeleteUserError? error)
        => error is null ? Results.NoContent() : error.Accept(problems);

    public IResult GetAdminGetStorageConfigsResult(List<UserStorageConfig>? configs)
        => configs is null ? Results.NotFound() : Results.Ok(configs.ConvertAll(ToAdminStorageConfigResponse));

    public IResult GetAdminCreateStorageConfigResult(ServiceResult<UserStorageConfig, IAddStorageConfigError> result)
        => result.Match(
            config => Results.Created(
                ApiPaths.Users.StorageConfigLocation(config.UserId, config.Id), ToAdminStorageConfigResponse(config)),
            error => error.Accept(problems));

    public IResult GetAdminDeleteStorageConfigResult(bool deleted)
        => deleted ? Results.NoContent() : Results.NotFound();

    public void ClearSessionCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(AuthMessages.RefreshTokenCookieName);
        http.Response.Cookies.Delete(ProxyEndpoints.MediaCookieName, SessionCookieOptions(ProxyEndpoints.MediaCookiePath));
    }

    private static AdminUserResponse ToAdminUserResponse(User user)
        => new(user.Id, user.Username, user.Email, user.IsAdmin, user.IsDisabled, user.StorageConfigs.Count);

    private static AdminStorageConfigResponse ToAdminStorageConfigResponse(UserStorageConfig config)
        => new(config.Id, config.UserId, config.RootPath, config.IsActive);

    private IResult BuildSessionResponse(AuthTokens tokens, HttpContext http, bool setCookie)
    {
        var user = UserProfileResponse.From(tokens.User);
        var expiresIn = (int)tokens.AccessTokenLifetime.TotalSeconds;
        if (!setCookie)
            return Results.Ok(new SessionResponse(tokens.AccessToken, expiresIn, user, tokens.RefreshToken));

        SetSessionCookies(http, tokens);
        return Results.Ok(new SessionResponse(tokens.AccessToken, expiresIn, user));
    }

    private void SetSessionCookies(HttpContext http, AuthTokens tokens)
    {
        var now = clock.GetCurrentInstant().ToDateTimeOffset();
        var refresh = SessionCookieOptions("/");
        refresh.Expires = now.AddDays(cookieSettings.RefreshTokenDays);
        http.Response.Cookies.Append(AuthMessages.RefreshTokenCookieName, tokens.RefreshToken, refresh);
        var media = SessionCookieOptions(ProxyEndpoints.MediaCookiePath);
        media.Expires = now.AddMinutes(cookieSettings.AccessTokenMinutes);
        http.Response.Cookies.Append(ProxyEndpoints.MediaCookieName, tokens.AccessToken, media);
    }

    private CookieOptions SessionCookieOptions(string path)
        => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = cookieSettings.SameSite,
            Path = path,
        };
}
