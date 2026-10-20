using Anichron.Infrastructure.Configuration;
using System.Security.Cryptography;

namespace Anichron.API.Infrastructure;

internal static class ApiIniDefaults
{
    internal static IReadOnlyList<IniEntry> Entries => [
        new IniEntry("Jwt", "Secret", () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))),
        new IniEntry("Jwt", "Issuer", () => "anichron-api"),
        new IniEntry("Jwt", "Audience", () => "anichron-client"),
        new IniEntry("Cors", "AllowedOrigins", () => string.Empty),
        new IniEntry("ForwardedHeaders", "KnownProxies", () => string.Empty),
        new IniEntry("ForwardedHeaders", "KnownNetworks", () => string.Empty),
        new IniEntry("WorkItemEvents", "IntervalSeconds", () => "2"),
    ];
}
