using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class CorsOriginsTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Read_CommaSeparatedOrigins_ReturnsThemTrimmedInOrder()
    {
        var configuration = Configuration(new() { ["Cors:AllowedOrigins"] = "https://a.example, https://b.example" });

        CorsOrigins.Read(configuration).Should().Equal("https://a.example", "https://b.example");
    }

    [Fact]
    public void Read_SectionMissing_ReturnsEmpty()
    {
        CorsOrigins.Read(Configuration([])).Should().BeEmpty();
    }

    [Fact]
    public void RefreshCookieSameSite_CrossOriginDeployment_IsNone()
    {
        CorsOrigins.RefreshCookieSameSite(["https://ui.example"]).Should().Be(SameSiteMode.None);
    }

    [Fact]
    public void RefreshCookieSameSite_SameOriginDeployment_IsStrict()
    {
        CorsOrigins.RefreshCookieSameSite([]).Should().Be(SameSiteMode.Strict);
    }

    [Fact]
    public void ConfigureDefaultPolicy_OriginsConfigured_AllowsThemWithCredentials()
    {
        var options = new CorsOptions();

        CorsOrigins.ConfigureDefaultPolicy(options, ["https://ui.example"]);

        var policy = options.GetPolicy(options.DefaultPolicyName);
        policy.Should().NotBeNull();
        policy.Origins.Should().Equal("https://ui.example");
        policy.SupportsCredentials.Should().BeTrue();
        policy.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
    }

    [Fact]
    public void ConfigureDefaultPolicy_NoOrigins_RegistersNoPolicy()
    {
        var options = new CorsOptions();

        CorsOrigins.ConfigureDefaultPolicy(options, []);

        options.GetPolicy(options.DefaultPolicyName).Should().BeNull();
    }
}
