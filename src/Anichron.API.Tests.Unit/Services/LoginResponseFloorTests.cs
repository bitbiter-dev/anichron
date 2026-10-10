using Anichron.API.Services;
using Microsoft.Extensions.Time.Testing;

namespace Anichron.API.Tests.Unit.Services;

public sealed class LoginResponseFloorTests
{
    private static readonly TimeSpan Floor = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider timeProvider = new();

    private LoginResponseFloor CreateTestee() => new(timeProvider, Floor);

    [Fact]
    public void PadAsync_WhenNoTimeHasElapsed_CompletesOnlyOnceTheFloorIsReached()
    {
        var testee = CreateTestee();
        var startedAt = testee.Start();

        var pad = testee.PadAsync(startedAt, CancellationToken.None);
        timeProvider.Advance(Floor - TimeSpan.FromTicks(1));
        var completedBeforeFloor = pad.IsCompleted;
        timeProvider.Advance(TimeSpan.FromTicks(1));

        Assert.Multiple(() =>
        {
            completedBeforeFloor.Should().BeFalse();
            pad.IsCompletedSuccessfully.Should().BeTrue();
        });
    }

    [Fact]
    public void PadAsync_SubtractsTimeAlreadyElapsedSinceStart()
    {
        var testee = CreateTestee();
        var startedAt = testee.Start();
        var elapsed = TimeSpan.FromMilliseconds(300);
        timeProvider.Advance(elapsed);

        var pad = testee.PadAsync(startedAt, CancellationToken.None);
        timeProvider.Advance(Floor - elapsed - TimeSpan.FromTicks(1));
        var completedBeforeFloor = pad.IsCompleted;
        timeProvider.Advance(TimeSpan.FromTicks(1));

        Assert.Multiple(() =>
        {
            completedBeforeFloor.Should().BeFalse();
            pad.IsCompletedSuccessfully.Should().BeTrue();
        });
    }

    [Fact]
    public void PadAsync_WhenElapsedEqualsTheFloor_CompletesImmediately()
    {
        var testee = CreateTestee();
        var startedAt = testee.Start();
        timeProvider.Advance(Floor);

        var pad = testee.PadAsync(startedAt, CancellationToken.None);

        pad.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void PadAsync_WhenElapsedExceedsTheFloor_CompletesImmediately()
    {
        var testee = CreateTestee();
        var startedAt = testee.Start();
        timeProvider.Advance(Floor + TimeSpan.FromSeconds(5));

        var pad = testee.PadAsync(startedAt, CancellationToken.None);

        pad.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task PadAsync_WhenCancelled_DoesNotWait()
    {
        var testee = CreateTestee();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => testee.PadAsync(testee.Start(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
