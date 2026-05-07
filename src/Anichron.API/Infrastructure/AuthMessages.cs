namespace Anichron.API.Infrastructure;

internal static class AuthMessages
{
    internal const string UsernameTaken = "Username is already taken.";
    internal const string EmailTaken = "An account with this email already exists.";
    internal const string InvalidEmail = "The provided email address is not valid.";
    internal const string PasswordPwned = "This password has appeared in a known data breach. Please choose a different one.";
    internal const string InvalidCredentials = "The username, email, or password is incorrect.";
    internal const string AccountDisabled = "This account has been disabled. Please contact an administrator.";

    internal static string AccountTemporarilyLocked(int secondsRemaining) => $"Too many failed login attempts. Please try again in {secondsRemaining} second{(secondsRemaining == 1 ? string.Empty : "s")}.";
    internal const string RefreshTokenRequired = "Refresh token is required.";
    internal const string RefreshTokenInvalid = "Refresh token is invalid or has expired.";
    internal const string TooManyRequests = "Too many requests. Please try again later.";
    internal const string RefreshTokenCookieName = "refresh_token";
}

internal static class AuthRateLimitPolicies
{
    internal const string Sensitive = "auth-sensitive";
    internal const string Refresh = "auth-refresh";
}
