using Anichron.Infrastructure.Configuration;
using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Anichron.API.Infrastructure;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddAppConfiguration()
        {
            var iniPath = Path.Combine(builder.Environment.ContentRootPath, "configuration", "app.ini");
            new AppIniInitializer(new FileSystem()).EnsureUpToDate(iniPath,
            [
                new IniEntry("Jwt", "Secret", () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))),
                new IniEntry("Jwt", "Issuer", () => "anichron-api"),
                new IniEntry("Jwt", "Audience", () => "anichron-client"),
                new IniEntry("Cors", "AllowedOrigins", () => string.Empty),
            ]);
            builder.Configuration.AddIniFile(iniPath, optional: false, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
            return builder;
        }
    }
}
