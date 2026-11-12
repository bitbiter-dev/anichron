using Anichron.API.Services;
using Anichron.API.Settings;
using Microsoft.Extensions.Options;

namespace Anichron.API.Endpoints;

public interface ISessionResponseMapper
{
    IResult GetRegistrationResult(ServiceResult<AuthTokens, IRegisterError> result, HttpContext http);
    IResult GetLoginResult(ServiceResult<AuthTokens, ILoginError> result, HttpContext http, bool setCookie);
    IResult GetRefreshResult(ServiceResult<AuthTokens, IRefreshError> result, HttpContext http);
    IResult GetChangePasswordResult(ServiceResult<AuthTokens, IChangePasswordError> result, HttpContext http);
    void ClearSessionCookies(HttpContext http);
}

public sealed class SessionResponseMapper(
    AuthCookieSettings cookieSettings,
    IClock clock,
    IOptions<UsernamePolicy> usernamePolicy,
    IOptions<PasswordPolicy> passwordPolicy) : ISessionResponseMapper
{
    private readonly ServiceProblems problems = new(usernamePolicy.Value, passwordPolicy.Value, ServiceProblems.Fields.Password);
    private readonly ServiceProblems changePasswordProblems = new(usernamePolicy.Value, passwordPolicy.Value, ServiceProblems.Fields.NewPassword);

    public IResult GetRegistrationResult(ServiceResult<AuthTokens, IRegisterError> result, HttpContext http)
        => result.Match(tokens => BuildSessionResponse(tokens, http, setCookie: true), error => error.Accept(problems));

    public IResult GetLoginResult(ServiceResult<AuthTokens, ILoginError> result, HttpContext http, bool setCookie)
        => result.Match(tokens => BuildSessionResponse(tokens, http, setCookie), error => error.Accept(problems));

    public IResult GetRefreshResult(ServiceResult<AuthTokens, IRefreshError> result, HttpContext http)
        => result.Match(tokens => BuildSessionResponse(tokens, http, CameWithRefreshCookie(http)), error => error.Accept(problems));

    public IResult GetChangePasswordResult(ServiceResult<AuthTokens, IChangePasswordError> result, HttpContext http)
        => result.Match(
            tokens => BuildSessionResponse(tokens, http, CameWithRefreshCookie(http)), error => error.Accept(changePasswordProblems));

    public void ClearSessionCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(AuthEndpoints.RefreshTokenCookieName);
        http.Response.Cookies.Delete(ProxyEndpoints.MediaCookieName, SessionCookieOptions(ProxyEndpoints.MediaCookiePath));
    }

    private static bool CameWithRefreshCookie(HttpContext http)
        => http.Request.Cookies.ContainsKey(AuthEndpoints.RefreshTokenCookieName);

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
        http.Response.Cookies.Append(AuthEndpoints.RefreshTokenCookieName, tokens.RefreshToken, refresh);
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
