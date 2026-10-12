using Anichron.API.Infrastructure;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ApiIniDefaultsTests
{
    [Fact]
    public void Entries_ExceptGeneratedSecret_HaveTheDocumentedDefaults()
    {
        var entries = ApiIniDefaults.Entries
            .Where(e => (e.Section, e.Key) != ("Jwt", "Secret"))
            .Select(e => (e.Section, e.Key, Value: e.DefaultValue()));

        entries.Should().Equal(
            ("Jwt", "Issuer", "anichron-api"),
            ("Jwt", "Audience", "anichron-client"),
            ("Cors", "AllowedOrigins", ""),
            ("ForwardedHeaders", "KnownProxies", ""),
            ("ForwardedHeaders", "KnownNetworks", ""));
    }

    [Fact]
    public void Entries_JwtSecret_IsFreshBase64Of64RandomBytes()
    {
        var secret = ApiIniDefaults.Entries.Single(e => (e.Section, e.Key) == ("Jwt", "Secret"));

        var first = secret.DefaultValue();
        var second = secret.DefaultValue();

        Convert.FromBase64String(first).Should().HaveCount(64);
        first.Should().NotBe(second);
    }
}
