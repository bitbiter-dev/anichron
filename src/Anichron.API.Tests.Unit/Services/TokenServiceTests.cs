using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Anichron.API.Tests.Unit.Services;

public sealed class TokenServiceTests
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 1, 1, 12, 0, 0);

    // Must be a valid base64 string because HashToken calls Convert.FromBase64String internally.
    private static readonly string ValidRawToken = Convert.ToBase64String(new byte[64]);

    private sealed class TestFixture
    {
        public IRefreshTokenRepository Tokens { get; } = Substitute.For<IRefreshTokenRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly IGuidFactory _guidFactory = Substitute.For<IGuidFactory>();
        private readonly IJwtFactory _jwtFactory = Substitute.For<IJwtFactory>();

        public TestFixture()
        {
            _clock.GetCurrentInstant().Returns(FixedNow);
            _guidFactory.NewGuid().Returns(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _jwtFactory.Create(Arg.Any<User>()).Returns("access_token");
        }

        public TestFixture CaptureAddedToken(Action<RefreshToken> capture)
        {
            Tokens.When(t => t.Add(Arg.Any<RefreshToken>())).Do(call => capture(call.Arg<RefreshToken>()));
            return this;
        }

        public TestFixture WithToken(RefreshToken token)
        {
            Tokens.FindByHashWithUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
            return this;
        }

        public TestFixture WithTokenByHash(RefreshToken token)
        {
            Tokens.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
            return this;
        }

        public TokenService CreateTestee() => new(
            Tokens, UnitOfWork, _clock, _guidFactory,
            Options.Create(new JwtSettings { RefreshTokenDays = 30 }),
            _jwtFactory);
    }

    private static RefreshToken ActiveToken(User user) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TokenHash = "any",
        CreatedAt = FixedNow,
        ExpiresAt = FixedNow + Duration.FromDays(30),
        User = user,
    };

    // ==========================================================================
    // IssueAsync
    // ==========================================================================

    [Fact]
    public async Task IssueAsync_ValidUser_ReturnsAccessTokenFromJwtFactory()
    {
        var user = new User { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var testee = new TestFixture().CreateTestee();

        var result = await testee.IssueAsync(user, CancellationToken.None);

        result.AccessToken.Should().Be("access_token");
    }

    [Fact]
    public async Task IssueAsync_ValidUser_AddsRefreshTokenWithCorrectProperties()
    {
        RefreshToken? captured = null;
        var user = new User { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var fixture = new TestFixture().CaptureAddedToken(t => captured = t);
        var testee = fixture.CreateTestee();

        await testee.IssueAsync(user, CancellationToken.None);

        Assert.Multiple(() =>
        {
            captured!.UserId.Should().Be(user.Id);
            captured.Id.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            captured.CreatedAt.Should().Be(FixedNow);
            captured.ExpiresAt.Should().Be(FixedNow + Duration.FromDays(30));
        });
    }

    [Fact]
    public async Task IssueAsync_ValidUser_StoredHashMatchesSha256OfRawToken()
    {
        RefreshToken? captured = null;
        var user = new User();
        var fixture = new TestFixture().CaptureAddedToken(t => captured = t);
        var testee = fixture.CreateTestee();

        var tokens = await testee.IssueAsync(user, CancellationToken.None);

        var expectedHash = Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(tokens.RefreshToken)));
        captured!.TokenHash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task IssueAsync_ValidUser_SavesChanges()
    {
        var user = new User();
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        await testee.IssueAsync(user, CancellationToken.None);

        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // RefreshAsync
    // ==========================================================================

    [Fact]
    public async Task RefreshAsync_TokenNotFound_ReturnsTokenInvalid()
    {
        var testee = new TestFixture().CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.TokenInvalid);
        });
    }

    [Fact]
    public async Task RefreshAsync_RevokedToken_RevokesAllSessionsAndReturnsTokenInvalid()
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var token = ActiveToken(user);
        token.RevokedAt = FixedNow - Duration.FromSeconds(1);
        var fixture = new TestFixture().WithToken(token);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.TokenInvalid);
        });
        await fixture.Tokens.Received(1)
            .RevokeAllActiveByUserIdAsync(user.Id, FixedNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsTokenInvalid()
    {
        var user = new User();
        var token = ActiveToken(user);
        token.ExpiresAt = FixedNow - Duration.FromSeconds(1);
        var fixture = new TestFixture().WithToken(token);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.TokenInvalid);
        });
    }

    [Fact]
    public async Task RefreshAsync_UserDisabled_ReturnsAccountDisabled()
    {
        var user = new User { IsDisabled = true };
        var fixture = new TestFixture().WithToken(ActiveToken(user));
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.AccountDisabled);
        });
    }

    [Fact]
    public async Task RefreshAsync_UserLocked_ReturnsLockedWithRetryAfterSeconds()
    {
        // Lock expires 60 s after FixedNow.
        var user = new User { LockedUntil = FixedNow + Duration.FromSeconds(60) };
        var fixture = new TestFixture().WithToken(ActiveToken(user));
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(AuthError.AccountTemporarilyLocked);
            result.RetryAfterSeconds.Should().Be(60);
        });
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RevokesOldTokenAndReturnsNewTokens()
    {
        var user = new User();
        var token = ActiveToken(user);
        var fixture = new TestFixture().WithToken(token);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            result.Value!.AccessToken.Should().Be("access_token");
            token.RevokedAt.Should().Be(FixedNow);
        });
    }

    // ==========================================================================
    // RevokeAsync
    // ==========================================================================

    [Fact]
    public async Task RevokeAsync_TokenNotFound_DoesNotSave()
    {
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        await testee.RevokeAsync(ValidRawToken, CancellationToken.None);

        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_DoesNotSave()
    {
        var token = ActiveToken(new User());
        token.RevokedAt = FixedNow - Duration.FromSeconds(1);
        var fixture = new TestFixture().WithTokenByHash(token);
        var testee = fixture.CreateTestee();

        await testee.RevokeAsync(ValidRawToken, CancellationToken.None);

        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_ActiveToken_SetsRevokedAtAndSaves()
    {
        var token = ActiveToken(new User());
        var fixture = new TestFixture().WithTokenByHash(token);
        var testee = fixture.CreateTestee();

        await testee.RevokeAsync(ValidRawToken, CancellationToken.None);

        token.RevokedAt.Should().Be(FixedNow);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
