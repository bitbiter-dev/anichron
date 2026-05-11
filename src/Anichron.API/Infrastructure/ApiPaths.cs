namespace Anichron.API.Infrastructure;

internal static class ApiPaths
{
    internal const string Base = "/api/v1";

    internal static class Auth
    {
        internal const string Group = "auth";
        internal const string Register = "/register";
        internal const string Login = "/login";
        internal const string LoginMobile = "/login/mobile";
        internal const string Refresh = "/refresh";
        internal const string Logout = "/logout";
        internal const string PasswordResetRequest = "/password-reset/request";
        internal const string PasswordResetConfirm = "/password-reset/confirm";
    }

    internal static class Users
    {
        internal const string Group = "users";
        internal const string ChangePassword = "/me/password";
    }
}
