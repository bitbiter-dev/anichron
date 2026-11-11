using Anichron.API.Security;
using Anichron.API.Services;
using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Time;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Anichron.API.Tests.Unit.Services;

public sealed class TokenServiceTests : IAsyncDisposable
{
    private static readonly Instant FixedNow = Instant.FromUtc(2026, 1, 1, 12, 0, 0);
    private static readonly Guid SuccessorId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    // Must be a valid base64 string because HashToken calls Convert.FromBase64String internally.
    private static readonly string ValidRawToken = Convert.ToBase64String(new byte[64]);

    private readonly Timeline timeline = new();

    public TokenServiceTests() => timeline.Clock.SetUtcNow(FixedNow.ToDateTimeOffset());

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private sealed class TestFixture
    {
        public IRefreshTokenRepository Tokens { get; } = Substitute.For<IRefreshTokenRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly IGuidFactory _guidFactory = Substitute.For<IGuidFactory>();
        private readonly IJwtFactory _jwtFactory = Substitute.For<IJwtFactory>();

        public TestFixture(Timeline? timeline = null)
        {
            if (timeline is null)
                _clock.GetCurrentInstant().Returns(FixedNow);
            else
                _clock.GetCurrentInstant().Returns(_ => Instant.FromDateTimeOffset(timeline.Clock.GetUtcNow()));
            _guidFactory.NewGuid().Returns(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _jwtFactory.Create(Arg.Any<User>()).Returns("access_token");
            Tokens.RotateAsync(Arg.Any<Guid>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>()).Returns(TokenRotation.Rotated);
        }

        public TestFixture CaptureAddedToken(Action<RefreshToken> capture)
        {
            Tokens.When(t => t.Add(Arg.Any<RefreshToken>())).Do(call => capture(call.Arg<RefreshToken>()));
            return this;
        }

        public TestFixture WithToken(RefreshToken token, params RefreshToken[] onLaterReads)
        {
            Tokens.FindByHashWithUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token, onLaterReads);
            return this;
        }

        public TestFixture LosingRotationsOf(Guid tokenId)
        {
            Tokens.RotateAsync(tokenId, Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>()).Returns(TokenRotation.Lost);
            return this;
        }

        public TestFixture WithTokenByHash(RefreshToken token)
        {
            Tokens.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(token);
            return this;
        }

        public TokenService CreateTestee() => new(
            Tokens, UnitOfWork, _clock, _guidFactory,
            Options.Create(new JwtSettings { AccessTokenMinutes = 20, RefreshTokenDays = 30 }),
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

    private static RefreshToken RotatedToken(User user, Instant rotatedAt, bool successorActive = true)
    {
        var token = ActiveToken(user);
        token.RevokedAt = rotatedAt;
        token.ReplacedBy = ActiveToken(user);
        token.ReplacedBy.Id = SuccessorId;
        token.ReplacedBy.CreatedAt = rotatedAt;
        token.ReplacedBy.RevokedAt = successorActive ? null : rotatedAt + Duration.FromSeconds(1);
        token.ReplacedById = SuccessorId;
        return token;
    }

    // ==========================================================================
    // Issue
    // ==========================================================================

    [Fact]
    public void Issue_ValidUser_ReturnsAccessTokenFromJwtFactory()
    {
        var user = new User { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var testee = new TestFixture().CreateTestee();

        var result = testee.Issue(user);

        result.AccessToken.Should().Be("access_token");
    }

    [Fact]
    public void Issue_ValidUser_ReturnsTheSessionForThatUserWithTheAccessTokenLifetimeInSeconds()
    {
        var user = new User { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var testee = new TestFixture().CreateTestee();

        var result = testee.Issue(user);

        Assert.Multiple(() =>
        {
            result.AccessTokenLifetime.Should().Be(TimeSpan.FromMinutes(20));
            result.User.Should().BeSameAs(user);
        });
    }

    [Fact]
    public void Issue_ValidUser_AddsRefreshTokenWithCorrectProperties()
    {
        RefreshToken? captured = null;
        var user = new User { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var fixture = new TestFixture().CaptureAddedToken(t => captured = t);
        var testee = fixture.CreateTestee();

        testee.Issue(user);

        Assert.Multiple(() =>
        {
            captured!.UserId.Should().Be(user.Id);
            captured.Id.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            captured.CreatedAt.Should().Be(FixedNow);
            captured.ExpiresAt.Should().Be(FixedNow + Duration.FromDays(30));
        });
    }

    [Fact]
    public void Issue_ValidUser_StoredHashMatchesSha256OfRawToken()
    {
        RefreshToken? captured = null;
        var user = new User();
        var fixture = new TestFixture().CaptureAddedToken(t => captured = t);
        var testee = fixture.CreateTestee();

        var tokens = testee.Issue(user);

        var expectedHash = Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(tokens.RefreshToken)));
        captured!.TokenHash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task Issue_ValidUser_StagesWithoutSaving()
    {
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        testee.Issue(new User());

        fixture.Tokens.Received(1).Add(Arg.Any<RefreshToken>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
            result.Error().Should().Be(new RefreshTokenInvalid());
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
            result.Error().Should().Be(new RefreshTokenInvalid());
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
            result.Error().Should().Be(new RefreshTokenInvalid());
        });
    }

    [Fact]
    public async Task RefreshAsync_TokenExpiringExactlyNow_ReturnsTokenInvalid()
    {
        var token = ActiveToken(new User());
        token.ExpiresAt = FixedNow;
        var fixture = new TestFixture().WithToken(token);

        var result = await fixture.CreateTestee().RefreshAsync(ValidRawToken, CancellationToken.None);

        result.Error().Should().Be(new RefreshTokenInvalid());
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
            result.Error().Should().Be(new AccountDisabled());
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
            result.Error().Should().Be(new AccountLocked(60));
        });
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesItIntoTheNewTokenAndReturnsTheNewSession()
    {
        RefreshToken? successor = null;
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var token = ActiveToken(user);
        var fixture = new TestFixture().WithToken(token);
        fixture.Tokens.RotateAsync(token.Id, Arg.Do<RefreshToken>(t => successor = t), Arg.Any<CancellationToken>())
            .Returns(TokenRotation.Rotated);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        var expectedHash = Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(result.Value().RefreshToken)));
        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            result.Value().AccessToken.Should().Be("access_token");
            result.Value().User.Should().BeSameAs(user);
            successor.Should().NotBeNull();
            successor.Id.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            successor.UserId.Should().Be(user.Id);
            successor.TokenHash.Should().Be(expectedHash);
            successor.CreatedAt.Should().Be(FixedNow);
            successor.ExpiresAt.Should().Be(FixedNow + Duration.FromDays(30));
        });
        fixture.Tokens.DidNotReceive().Add(Arg.Any<RefreshToken>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_RotationThrows_PropagatesException()
    {
        var token = ActiveToken(new User());
        var fixture = new TestFixture().WithToken(token);
        fixture.Tokens.RotateAsync(Arg.Any<Guid>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TokenRotation>(new InvalidOperationException("DB error")));
        var testee = fixture.CreateTestee();

        var act = async () => await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ==========================================================================
    // RefreshAsync: a just-rotated token (ADR 0017)
    // ==========================================================================

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    public async Task RefreshAsync_TokenRotatedLastWithinTheWindow_RotatesItsSuccessorAndReturnsANewSession(int secondsLater)
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var fixture = new TestFixture(timeline).WithToken(RotatedToken(user, FixedNow));
        var testee = fixture.CreateTestee();
        timeline.Clock.Advance(TimeSpan.FromSeconds(secondsLater));

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        Assert.Multiple(() =>
        {
            result.IsSuccess.Should().BeTrue();
            result.Value().User.Should().BeSameAs(user);
        });
        await fixture.Tokens.Received(1).RotateAsync(
            SuccessorId,
            Arg.Is<RefreshToken>(t => t.UserId == user.Id && t.CreatedAt == FixedNow + Duration.FromSeconds(secondsLater)),
            Arg.Any<CancellationToken>());
        await fixture.Tokens.DidNotReceive()
            .RevokeAllActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_TokenRotatedLastAfterTheWindow_RevokesEverySessionAndReturnsTokenInvalid()
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var fixture = new TestFixture(timeline).WithToken(RotatedToken(user, FixedNow));
        var testee = fixture.CreateTestee();
        timeline.Clock.Advance(TimeSpan.FromSeconds(31));

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        result.Error().Should().Be(new RefreshTokenInvalid());
        await fixture.Tokens.Received(1).RevokeAllActiveByUserIdAsync(
            user.Id, FixedNow + Duration.FromSeconds(31), Arg.Any<CancellationToken>());
        await fixture.Tokens.DidNotReceive()
            .RotateAsync(Arg.Any<Guid>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_OlderTokenWithinTheWindow_RevokesEverySessionAndReturnsTokenInvalid()
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var fixture = new TestFixture(timeline).WithToken(RotatedToken(user, FixedNow, successorActive: false));
        var testee = fixture.CreateTestee();
        timeline.Clock.Advance(TimeSpan.FromSeconds(2));

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        result.Error().Should().Be(new RefreshTokenInvalid());
        await fixture.Tokens.Received(1).RevokeAllActiveByUserIdAsync(
            user.Id, FixedNow + Duration.FromSeconds(2), Arg.Any<CancellationToken>());
        await fixture.Tokens.DidNotReceive()
            .RotateAsync(Arg.Any<Guid>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_TokenRotatedLastForADisabledUser_ReturnsAccountDisabledWithoutRotating()
    {
        var user = new User { IsDisabled = true };
        var fixture = new TestFixture(timeline).WithToken(RotatedToken(user, FixedNow));
        var testee = fixture.CreateTestee();
        timeline.Clock.Advance(TimeSpan.FromSeconds(1));

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        result.Error().Should().Be(new AccountDisabled());
        await fixture.Tokens.DidNotReceive()
            .RotateAsync(Arg.Any<Guid>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_ASimultaneousRefreshRotatedTheTokenFirst_RotatesItsSuccessorInstead()
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var presented = ActiveToken(user);
        var rotatedMeanwhile = RotatedToken(user, FixedNow);
        rotatedMeanwhile.Id = presented.Id;
        var fixture = new TestFixture(timeline).WithToken(presented, rotatedMeanwhile).LosingRotationsOf(presented.Id);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await fixture.Tokens.Received(1).RotateAsync(SuccessorId, Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await fixture.Tokens.DidNotReceive()
            .RevokeAllActiveByUserIdAsync(Arg.Any<Guid>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_ASimultaneousRefreshRotatedTheSuccessorFirst_RevokesEverySessionAndReturnsTokenInvalid()
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var fixture = new TestFixture(timeline).WithToken(RotatedToken(user, FixedNow)).LosingRotationsOf(SuccessorId);
        var testee = fixture.CreateTestee();
        timeline.Clock.Advance(TimeSpan.FromSeconds(1));

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        result.Error().Should().Be(new RefreshTokenInvalid());
        await fixture.Tokens.Received(1).RevokeAllActiveByUserIdAsync(
            user.Id, FixedNow + Duration.FromSeconds(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_TokenStillActiveAfterLosingItsRotation_RevokesEverySessionAfterOneRetry()
    {
        var user = new User { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var presented = ActiveToken(user);
        var fixture = new TestFixture().WithToken(presented, presented, null!).LosingRotationsOf(presented.Id);
        var testee = fixture.CreateTestee();

        var result = await testee.RefreshAsync(ValidRawToken, CancellationToken.None);

        result.Error().Should().Be(new RefreshTokenInvalid());
        await fixture.Tokens.Received(2).RotateAsync(presented.Id, Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await fixture.Tokens.Received(1).RevokeAllActiveByUserIdAsync(user.Id, FixedNow, Arg.Any<CancellationToken>());
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

    // ==========================================================================
    // MarkAllSessionsRevokedAsync
    // ==========================================================================

    [Fact]
    public async Task MarkAllSessionsRevokedAsync_CallsRevokeAllActiveByUserIdWithoutSaving()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fixture = new TestFixture();
        var testee = fixture.CreateTestee();

        await testee.MarkAllSessionsRevokedAsync(userId, FixedNow, CancellationToken.None);

        await fixture.Tokens.Received(1).RevokeAllActiveByUserIdAsync(userId, FixedNow, Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
