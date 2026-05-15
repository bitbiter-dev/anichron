namespace Anichron.API.Infrastructure;

internal static class ApiPaths
{
    internal const string Base = "/api/v1";
    internal const string Healthz = $"{Base}/healthz";

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

        internal const string LogoutPath = $"{Base}/{Group}{Logout}";
    }

    internal static class Users
    {
        internal const string Group = "users";
        internal const string Me = "/me";
        internal const string ChangePassword = $"{Me}/password";
        internal const string StorageConfigs = "storage-configs";

        internal const string ById = "{userId:guid}";
        internal const string PasswordReset = "{userId:guid}/password-reset";
        internal const string UserStorageConfigs = $"{{userId:guid}}/{StorageConfigs}";
        internal const string UserStorageConfigById = $"{{userId:guid}}/{StorageConfigs}/{{configId:guid}}";

        internal const string MePath = $"{Base}/{Group}{Me}";
        internal const string ChangePasswordPath = $"{Base}/{Group}{ChangePassword}";

        internal static string UserLocation(Guid userId)
            => $"{Base}/{Group}/{userId}";

        internal static string StorageConfigLocation(Guid userId, Guid configId)
            => $"{Base}/{Group}/{userId}/{StorageConfigs}/{configId}";
    }
}
