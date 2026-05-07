using Anichron.API.Security;
using Anichron.API.Settings;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Anichron.API.Tests.Unit.Security;

public sealed class JwtFactoryTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 1, 1, 12, 0, 0);
    private static readonly Guid FixedJti = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // 32+ bytes required for HMAC-SHA256.
    private const string TestSecret = "test-secret-that-is-long-enough-for-hmac-sha256-signing";

    private sealed class TestFixture
    {
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly IGuidFactory _guidFactory = Substitute.For<IGuidFactory>();
        private readonly JwtSettings _settings = new()
        {
            Secret = TestSecret,
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenMinutes = 15,
        };

        public TestFixture()
        {
            _clock.GetCurrentInstant().Returns(FixedNow);
            _guidFactory.NewGuid().Returns(FixedJti);
        }

        public JwtFactory CreateTestee() => new(Options.Create(_settings), _clock, _guidFactory);
    }

    private static JwtSecurityToken Decode(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token);

    // Validates signature and returns the parsed token; throws if the signature is invalid.
    private static JwtSecurityToken ValidateSignature(string token, string secret)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
        }, out var validated);
        return (JwtSecurityToken)validated;
    }

    // ==========================================================================
    // Create
    // ==========================================================================

    [Fact]
    public void Create_ValidUser_ReturnsWellFormedJwt()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var token = testee.Create(user);

        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void Create_ValidUser_SubjectClaimIsUserId()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = new User { Id = userId, Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        jwt.Subject.Should().Be(userId.ToString());
    }

    [Fact]
    public void Create_ValidUser_UniqueNameClaimIsUsername()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.UniqueName)?.Value
            .Should().Be("alice");
    }

    [Fact]
    public void Create_ValidUser_JtiClaimIsFromGuidFactory()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        jwt.Id.Should().Be(FixedJti.ToString());
    }

    [Fact]
    public void Create_ValidUser_IssuerAndAudienceMatchSettings()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        Assert.Multiple(() =>
        {
            jwt.Issuer.Should().Be("test-issuer");
            jwt.Audiences.Should().Contain("test-audience");
        });
    }

    [Fact]
    public void Create_ValidUser_ExpiryIsNowPlusAccessTokenMinutes()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        jwt.ValidTo.Should().Be(FixedNow.ToDateTimeUtc().AddMinutes(15));
    }

    [Fact]
    public void Create_MustChangePasswordTrue_IncludesMustChangePasswordClaim()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice", MustChangePassword = true };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        jwt.Claims.FirstOrDefault(c => c.Type == "must_change_password")?.Value
            .Should().Be("true");
    }

    [Fact]
    public void Create_MustChangePasswordFalse_DoesNotIncludeMustChangePasswordClaim()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice", MustChangePassword = false };
        var testee = new TestFixture().CreateTestee();

        var jwt = Decode(testee.Create(user));

        jwt.Claims.Should().NotContain(c => c.Type == "must_change_password");
    }

    [Fact]
    public void Create_ValidUser_TokenSignatureValidatesWithConfiguredSecret()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var token = testee.Create(user);

        var act = () => ValidateSignature(token, TestSecret);
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_ValidUser_TokenSignatureFailsWithWrongSecret()
    {
        var user = new User { Id = Guid.NewGuid(), Username = "alice" };
        var testee = new TestFixture().CreateTestee();

        var token = testee.Create(user);

        var act = () => ValidateSignature(token, "wrong-secret-that-is-also-long-enough-to-use");
        act.Should().Throw<SecurityTokenInvalidSignatureException>();
    }
}
