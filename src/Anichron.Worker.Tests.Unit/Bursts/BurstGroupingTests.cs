using Anichron.Core.Domain;
using Anichron.Worker.Bursts;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Bursts;

public sealed class BurstGroupingTests
{
    private const int Span = 300;

    private static LocalDateTime Noon => new(2024, 7, 15, 12, 0, 0);

    private static MediaAsset Photo(int seconds, Guid? id = null, bool softDeleted = false) => new()
    {
        Id = id ?? Guid.NewGuid(),
        DateCaptured = Noon.PlusSeconds(seconds),
        IsSoftDeleted = softDeleted,
    };

    private static Burst BurstOf(Instant createdAt, params MediaAsset[] members)
    {
        var burst = new Burst { Id = Guid.NewGuid(), CreatedAt = createdAt, Assets = [.. members] };
        foreach (var member in members)
            member.BurstId = burst.Id;
        return burst;
    }

    private static Instant At(int seconds) => Instant.FromUtc(2024, 7, 16, 0, 0) + Duration.FromSeconds(seconds);

    [Fact]
    public void Join_NoNeighbours_JoinsNothing()
        => BurstGrouping.Join(Photo(0), [], [], [], Span).Should().BeNull();

    [Fact]
    public void Join_SimilarNeighbourWithoutABurst_JoinsBothIntoANewBurst()
    {
        var (photo, neighbour) = (Photo(10), Photo(0));

        var join = BurstGrouping.Join(photo, [], [neighbour], [], Span);

        join.Should().NotBeNull();
        join.Members.Should().BeEquivalentTo([photo, neighbour]);
        join.Target.Should().BeNull();
        join.Merged.Should().BeEmpty();
        join.Cover.Should().BeSameAs(neighbour);
    }

    [Fact]
    public void Join_SimilarNeighbourInABurst_JoinsThatBurstWithAllItsMembers()
    {
        var (photo, neighbour, other) = (Photo(20), Photo(10), Photo(0));
        var burst = BurstOf(At(0), neighbour, other);

        var join = BurstGrouping.Join(photo, [], [neighbour], [burst], Span);

        join.Should().NotBeNull();
        join.Members.Should().BeEquivalentTo([photo, neighbour, other]);
        join.Target.Should().BeSameAs(burst);
        join.Merged.Should().BeEmpty();
        join.Cover.Should().BeSameAs(other);
    }

    [Fact]
    public void Join_SimilarNeighbourExactlyAtTheSpan_Joins()
    {
        var (photo, neighbour) = (Photo(300), Photo(0));

        BurstGrouping.Join(photo, [], [neighbour], [], Span)!.Members.Should().BeEquivalentTo([photo, neighbour]);
    }

    [Fact]
    public void Join_SimilarNeighbourPastTheSpan_JoinsNothing()
    {
        var (photo, neighbour) = (Photo(301), Photo(0));

        BurstGrouping.Join(photo, [], [neighbour], [], Span).Should().BeNull();
    }

    [Fact]
    public void Join_SimilarNeighbourWhoseBurstWouldPassTheSpan_IsLeftOut()
    {
        var (photo, neighbour, farMember) = (Photo(200), Photo(150), Photo(-101));
        var burst = BurstOf(At(0), neighbour, farMember);

        BurstGrouping.Join(photo, [], [neighbour], [burst], Span).Should().BeNull();
    }

    [Fact]
    public void Join_SoftDeletedMemberPastTheSpan_DoesNotCountTowardsIt()
    {
        var (photo, neighbour, trashed) = (Photo(200), Photo(150), Photo(-101, softDeleted: true));
        var burst = BurstOf(At(0), neighbour, trashed);

        var join = BurstGrouping.Join(photo, [], [neighbour], [burst], Span);

        join.Should().NotBeNull();
        join.Members.Should().BeEquivalentTo([photo, neighbour, trashed]);
        join.Cover.Should().BeSameAs(neighbour);
    }

    [Fact]
    public void Join_EarlierAcceptedNeighbour_CanPushALaterOnePastTheSpan()
    {
        var (photo, early, late) = (Photo(0), Photo(-200), Photo(200));

        var join = BurstGrouping.Join(photo, [], [early, late], [], Span);

        join.Should().NotBeNull();
        join.Members.Should().BeEquivalentTo([photo, early]);
    }

    [Fact]
    public void Join_LabelledNeighbours_JoinWhateverTheirSpan()
    {
        var (photo, labelled, member) = (Photo(0), Photo(-1000), Photo(-2000));
        var burst = BurstOf(At(0), labelled, member);

        var join = BurstGrouping.Join(photo, [labelled], [], [burst], Span);

        join.Should().NotBeNull();
        join.Members.Should().BeEquivalentTo([photo, labelled, member]);
        join.Target.Should().BeSameAs(burst);
        join.Cover.Should().BeSameAs(member);
    }

    [Fact]
    public void Join_LabelledGroupAlreadyWiderThanTheSpan_AdmitsNoSimilarNeighbour()
    {
        var (photo, labelled, near) = (Photo(0), Photo(-1000), Photo(10));

        var join = BurstGrouping.Join(photo, [labelled], [near], [], Span);

        join.Should().NotBeNull();
        join.Members.Should().BeEquivalentTo([photo, labelled]);
    }

    [Fact]
    public void Join_SeveralBursts_MergeIntoTheOldest()
    {
        var (photo, inNewer, inOlder, inSameAgeHigherId) = (Photo(0), Photo(10), Photo(20), Photo(30));
        var older = BurstOf(At(0), inOlder);
        var newer = BurstOf(At(60), inNewer);
        var sameAgeHigherId = BurstOf(At(0), inSameAgeHigherId);
        older.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        sameAgeHigherId.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        (inOlder.BurstId, inSameAgeHigherId.BurstId) = (older.Id, sameAgeHigherId.Id);

        var join = BurstGrouping.Join(photo, [], [inNewer, inSameAgeHigherId, inOlder], [newer, sameAgeHigherId, older], Span);

        join.Should().NotBeNull();
        join.Target.Should().BeSameAs(older);
        join.Merged.Should().Equal(sameAgeHigherId, newer);
    }

    [Fact]
    public void Join_BurstOfARejectedNeighbour_IsNeitherTargetNorMerged()
    {
        var (photo, near, far) = (Photo(0), Photo(10), Photo(400));
        var rejected = BurstOf(At(0), far);

        var join = BurstGrouping.Join(photo, [], [near, far], [rejected], Span);

        join.Should().NotBeNull();
        join.Target.Should().BeNull();
        join.Merged.Should().BeEmpty();
    }

    [Fact]
    public void Cover_IsTheEarliestActiveMemberThenTheLowestId()
    {
        var low = Photo(0, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = Photo(0, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));

        BurstGrouping.Cover([high, Photo(-10, softDeleted: true), low, Photo(5)]).Should().BeSameAs(low);
    }
}
