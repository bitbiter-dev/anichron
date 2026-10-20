using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class DuplicateCopyTallyTests
{
    private static readonly Guid Home = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid Phone = Guid.Parse("22222222-0000-0000-0000-000000000000");

    [Fact]
    public void Take_AfterCopiesCounted_ReturnsTheirNumberForThatConfigOnly()
    {
        var tally = new DuplicateCopyTally();
        tally.Count(Home);
        tally.Count(Home);
        tally.Count(Phone);

        tally.Take(Home).Should().Be(2);
        tally.Take(Phone).Should().Be(1);
    }

    [Fact]
    public void Take_Twice_StartsTheNextCycleFromZero()
    {
        var tally = new DuplicateCopyTally();
        tally.Count(Home);
        tally.Take(Home);

        tally.Take(Home).Should().Be(0);
    }

    [Fact]
    public void Count_FromConcurrentConsumers_LosesNoCopy()
    {
        var tally = new DuplicateCopyTally();

        Parallel.For(0, 1000, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken }, _ => tally.Count(Home));

        tally.Take(Home).Should().Be(1000);
    }
}
