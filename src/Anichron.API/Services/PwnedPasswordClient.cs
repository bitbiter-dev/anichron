using System.Security.Cryptography;
using System.Text;

namespace Anichron.API.Services;

public interface IPwnedPasswordClient
{
    Task<bool> IsPwnedAsync(string password, CancellationToken ct);
}

public sealed partial class PwnedPasswordClient(HttpClient http, ILogger<PwnedPasswordClient> logger) : IPwnedPasswordClient
{
    public async Task<bool> IsPwnedAsync(string password, CancellationToken ct)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            // SHA1 is not a choice here: the HIBP range endpoint is defined in terms of SHA-1
            // prefixes, so any other algorithm queries a namespace the service does not have.
            // S4790 is Sonar's equivalent of CA5350 and arrived with SonarAnalyzer 10.34; both
            // are listed so neither analyzer alone fails the build.
#pragma warning disable CA5350, S4790 // SHA1 is required by the HIBP k-anonymity API protocol
            var hash = Convert.ToHexString(SHA1.HashData(passwordBytes));
#pragma warning restore CA5350, S4790
            var prefix = hash[..5];
            var suffix = hash[5..];

            var body = await http.GetStringAsync($"range/{prefix}", ct);
            return body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(line =>
                line.StartsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.PwnedCheckUnavailable(logger, ex, ex.GetType().Name);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Pwned Passwords check unavailable ({ExceptionType}); failing open.")]
        public static partial void PwnedCheckUnavailable(ILogger logger, Exception ex, string exceptionType);
    }
}
