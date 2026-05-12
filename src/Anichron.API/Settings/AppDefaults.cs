using System.Diagnostics.CodeAnalysis;

namespace Anichron.API.Settings;

[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded")]
internal static class AppDefaults
{
    internal static class Pwned
    {
        internal const string Url = "https://api.pwnedpasswords.com/";
        internal const int TimeoutInSeconds = 5;
    }

    internal static class Jwt
    {
        internal const int AccessTokenMinutes = 15;
        internal const int RefreshTokenDays = 60;
    }

    internal static class Password
    {
        internal const int MinLength = 12;
        internal const int MaxLength = 128;
        internal const bool CheckPwned = true;
    }

    internal static class Username
    {
        internal const int MinLength = 3;
        internal const int MaxLength = 32;
    }

    internal static class Email
    {
        internal const int MaxLength = 256;
    }

    internal static class Argon2
    {
        internal const int Parallelism = 4;
        internal const int Iterations = 3;
        internal const int MemoryKiB = 65_536;
        internal const int SaltLength = 16;
        internal const int HashLength = 32;
    }

    internal static class RateLimit
    {
        internal static class Sensitive
        {
            internal const int PermitLimit = 10;
            internal const int WindowSeconds = 60;
            internal const int Segments = 6;
        }

        internal static class Refresh
        {
            internal const int PermitLimit = 5;
            internal const int WindowMinutes = 15;
            internal const int Segments = 3;
        }
    }

    internal static class Lockout
    {
        internal const int AllowedAttempts = 3;
        internal const int MaxAttempts = 12;
        internal const int MaxSeconds = 300;
        internal const int BackoffBase = 2;
    }

    internal static class Storage
    {
        internal const string ProxyPath = "/data/proxies";
    }

    internal static class Startup
    {
        internal const int MaxDbRetryAttempts = 10;
        internal const int DbRetryDelaySeconds = 5;
        internal const string AdminDefaultUsername = "admin";
        internal const string AdminDefaultMail = "admin@localhost";
        // Safe: MustChangePassword = true forces rotation on first login; this value is only
        // used when no users exist and no BOOTSTRAP_ADMIN_PASSWORD env var is configured.
#pragma warning disable S2068
        internal const string AdminDefaultPassword = "admin";
#pragma warning restore S2068
    }
}
