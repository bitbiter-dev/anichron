using Anichron.API.Services;
using Anichron.Core.Data;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Services;

public sealed class LockoutServiceTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 1, 12, 0, 0);

    private sealed class TestFixture
    {
        internal readonly IUnitOfWork UnitOfWork = Substitute.For<IUnitOfWork>();

        public LockoutService Build() => new(UnitOfWork);
    }

    // ==========================================================================
    // IsLockedOut
    // ==========================================================================

    [Fact]
    public void IsLockedOut_LockedUntilInFuture_ReturnsTrue()
    {
        var user = new User { LockedUntil = Now.Plus(Duration.FromSeconds(30)) };
        var sut = new TestFixture().Build();

        sut.IsLockedOut(user, Now).Should().BeTrue();
    }

    [Fact]
    public void IsLockedOut_LockedUntilInPast_ReturnsFalse()
    {
        var user = new User { LockedUntil = Now.Minus(Duration.FromSeconds(1)) };
        var sut = new TestFixture().Build();

        sut.IsLockedOut(user, Now).Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_LockedUntilExactlyNow_ReturnsFalse()
    {
        var user = new User { LockedUntil = Now };
        var sut = new TestFixture().Build();

        sut.IsLockedOut(user, Now).Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_LockedUntilNull_ReturnsFalse()
    {
        var user = new User { LockedUntil = null };
        var sut = new TestFixture().Build();

        sut.IsLockedOut(user, Now).Should().BeFalse();
    }

    // ==========================================================================
    // RecordFailedAttemptAsync
    // ==========================================================================

    [Fact]
    public async Task RecordFailedAttemptAsync_IncrementsFailedLoginAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User { FailedLoginAttempts = 2 };
        var sut = new TestFixture().Build();

        await sut.RecordFailedAttemptAsync(user, Now, ct);

        user.FailedLoginAttempts.Should().Be(3);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_SetsLockedUntilBasedOnBackoff()
    {
        var ct = TestContext.Current.CancellationToken;
        // 3 existing → 4th attempt → backoff = 2^(4-3) = 2 s
        var user = new User { FailedLoginAttempts = 3 };
        var sut = new TestFixture().Build();

        await sut.RecordFailedAttemptAsync(user, Now, ct);

        user.LockedUntil.Should().Be(Now.Plus(Duration.FromSeconds(2)));
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_SavesChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new User();
        var fixture = new TestFixture();
        var sut = fixture.Build();

        await sut.RecordFailedAttemptAsync(user, Now, ct);

        await fixture.UnitOfWork.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_BelowThreshold_SetsLockedUntilToNow()
    {
        var ct = TestContext.Current.CancellationToken;
        // 1st attempt (≤ AllowedAttempts=3) → backoff is 0 → LockedUntil set to Now, not null
        var user = new User { FailedLoginAttempts = 0 };
        var sut = new TestFixture().Build();

        await sut.RecordFailedAttemptAsync(user, Now, ct);

        user.LockedUntil.Should().Be(Now);
    }

    // ==========================================================================
    // PrepareReset
    // ==========================================================================

    [Fact]
    public void PrepareReset_ClearsCounterAndLockedUntil()
    {
        var user = new User
        {
            FailedLoginAttempts = 5,
            LockedUntil = Now.Plus(Duration.FromSeconds(30)),
        };
        var sut = new TestFixture().Build();

        sut.PrepareReset(user);

        Assert.Multiple(() =>
        {
            user.FailedLoginAttempts.Should().Be(0);
            user.LockedUntil.Should().BeNull();
        });
    }

    // ==========================================================================
    // ComputeBackoffSeconds — AllowedAttempts=3, MaxAttempts=12, MaxSeconds=300, Base=2
    // failedAttempts is the post-increment count (already stored on the user when this is called).
    // ==========================================================================

    [Theory]
    [InlineData(1, 0)]    // 1st attempt (≤ AllowedAttempts=3) → no lockout
    [InlineData(3, 0)]    // 3rd attempt (= AllowedAttempts) → no lockout
    [InlineData(4, 2)]    // 4th attempt → 2^(4-3) = 2 s
    [InlineData(5, 4)]    // 5th attempt → 2^(5-3) = 4 s
    [InlineData(11, 256)] // 11th attempt → 2^(11-3) = 256 s
    [InlineData(12, 300)] // 12th attempt (= MaxAttempts) → capped at MaxSeconds
    [InlineData(16, 300)] // beyond MaxAttempts → still capped
    public void ComputeBackoffSeconds_ReturnsExpectedSeconds(int failedAttempts, int expectedSeconds)
    {
        LockoutService.ComputeBackoffSeconds(failedAttempts).Should().Be(expectedSeconds);
    }
}
