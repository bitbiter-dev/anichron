using Anichron.API.Infrastructure;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class CorsConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static CorsPolicy? DefaultPolicy(IConfiguration configuration)
    {
        using var provider = new ServiceCollection().AddCorsPolicy(configuration).BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        return options.GetPolicy(options.DefaultPolicyName);
    }

    [Fact]
    public void AddCorsPolicy_CommaSeparatedEnvVarStyleKey_AllowsEachOrigin()
    {
        var policy = DefaultPolicy(Config(new() { ["CORS:ALLOWEDORIGINS"] = "http://a.test, http://b.test" }));

        policy.Should().NotBeNull();
        policy.Origins.Should().Equal("http://a.test", "http://b.test");
    }

    [Fact]
    public void AddCorsPolicy_NoOrigins_RegistersNoDefaultPolicy()
        => DefaultPolicy(Config([])).Should().BeNull();

    private static SameSiteMode CookieSameSite(Dictionary<string, string?> settings)
    {
        settings["Jwt:Secret"] = new string('s', 32);
        settings["Jwt:Issuer"] = "issuer";
        settings["Jwt:Audience"] = "audience";
        using var provider = new ServiceCollection().AddAuthServices(Config(settings)).BuildServiceProvider();
        return provider.GetRequiredService<AuthCookieSettings>().SameSite;
    }

    [Fact]
    public void AddAuthServices_CommaSeparatedOrigins_UsesSameSiteNone()
        => CookieSameSite(new() { ["CORS:ALLOWEDORIGINS"] = "http://a.test, http://b.test" })
            .Should().Be(SameSiteMode.None);

    [Fact]
    public void AddAuthServices_NoOrigins_UsesSameSiteStrict()
        => CookieSameSite([]).Should().Be(SameSiteMode.Strict);
}
