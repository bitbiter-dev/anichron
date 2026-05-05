namespace Anichron.Core.Services;

public sealed record AuthTokens(string AccessToken, string RefreshToken);

public enum AuthError
{
    UsernameTaken = 0,
    EmailTaken = 1,
    InvalidCredentials = 2,
    TokenInvalid = 3,
}

public sealed record AuthResult<T>
{
    public T? Value { get; init; }
    public AuthError? Error { get; init; }
    public bool IsSuccess => Error is null;
}

public static class AuthResult
{
    public static AuthResult<T> Ok<T>(T value) => new() { Value = value };
    public static AuthResult<T> Fail<T>(AuthError error) => new() { Error = error };
}

public interface IAuthService
{
    Task<AuthResult<AuthTokens>> RegisterAsync(string username, string email, string password, CancellationToken ct = default);
    Task<AuthResult<AuthTokens>> LoginAsync(string usernameOrEmail, string password, CancellationToken ct = default);
    Task<AuthResult<AuthTokens>> RefreshAsync(string rawToken, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, CancellationToken ct = default);
}
