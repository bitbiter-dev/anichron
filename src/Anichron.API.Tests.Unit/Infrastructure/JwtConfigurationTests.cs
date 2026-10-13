using Anichron.API.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class JwtConfigurationTests
{
    private const string ValidSecret = "0123456789abcdef0123456789abcdef";

    private static IConfiguration Configuration(string? secret = ValidSecret, string? issuer = "iss", string? audience = "aud")
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret,
                ["Jwt:Issuer"] = issuer,
                ["Jwt:Audience"] = audience,
            })
            .Build();

    [Fact]
    public void Read_AllValuesPresent_ReturnsThem()
    {
        var result = JwtConfiguration.Read(Configuration());

        result.Should().Be(new JwtConfiguration(ValidSecret, "iss", "aud"));
    }

    [Fact]
    public void Read_SecretMissing_Throws()
    {
        var act = () => JwtConfiguration.Read(Configuration(secret: null));

        act.Should().Throw<InvalidOperationException>().WithMessage("Jwt:Secret configuration is missing.");
    }

    [Fact]
    public void Read_IssuerMissing_Throws()
    {
        var act = () => JwtConfiguration.Read(Configuration(issuer: null));

        act.Should().Throw<InvalidOperationException>().WithMessage("Jwt:Issuer is missing.");
    }

    [Fact]
    public void Read_AudienceMissing_Throws()
    {
        var act = () => JwtConfiguration.Read(Configuration(audience: null));

        act.Should().Throw<InvalidOperationException>().WithMessage("Jwt:Audience is missing.");
    }

    [Fact]
    public void Read_SecretOneByteShortOfMinimum_Throws()
    {
        var act = () => JwtConfiguration.Read(Configuration(secret: ValidSecret[1..]));

        act.Should().Throw<InvalidOperationException>().WithMessage("Jwt:Secret must be at least 32 bytes.");
    }

    [Fact]
    public void Read_SecretLengthCountedInUtf8Bytes_AcceptsSixteenTwoByteCharacters()
    {
        var secret = new string('é', 16);

        var result = JwtConfiguration.Read(Configuration(secret: secret));

        result.Secret.Should().Be(secret);
    }
}
