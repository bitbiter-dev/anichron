using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class IngestionClassificationTests
{
    private static int DuplicateCopies(FileOutcome primary, FileOutcome? secondary = null)
        => Classifications.Of(primary, secondary).DuplicateCopies;

    [Fact]
    public void DuplicateCopies_DuplicateSingleFile_IsOne()
        => DuplicateCopies(FileOutcome.DuplicateCopy).Should().Be(1);

    [Fact]
    public void DuplicateCopies_BothHalvesDuplicates_IsTwo()
        => DuplicateCopies(FileOutcome.DuplicateCopy, FileOutcome.DuplicateCopy).Should().Be(2);

    [Fact]
    public void DuplicateCopies_OnlyTheMovDuplicate_IsOne()
        => DuplicateCopies(FileOutcome.Unchanged, FileOutcome.DuplicateCopy).Should().Be(1);

    [Fact]
    public void DuplicateCopies_OnlyThePrimaryDuplicate_IsOne()
        => DuplicateCopies(FileOutcome.DuplicateCopy, FileOutcome.Moved).Should().Be(1);

    [Fact]
    public void DuplicateCopies_KnownContentWithoutCopies_IsZero()
        => (DuplicateCopies(FileOutcome.Moved), DuplicateCopies(FileOutcome.Unchanged, FileOutcome.Unchanged))
            .Should().Be((0, 0));

    [Fact]
    public void DuplicateCopies_NewPrimaryWithADuplicateMov_IsZero()
        => DuplicateCopies(FileOutcome.New, FileOutcome.DuplicateCopy).Should().Be(0);

    [Fact]
    public void DuplicateCopies_ChangedPrimaryWithADuplicateMov_IsZero()
        => DuplicateCopies(FileOutcome.ContentChanged, FileOutcome.DuplicateCopy).Should().Be(0);

    [Fact]
    public void DuplicateCopies_OverwrittenByCopy_IsOne()
        => DuplicateCopies(FileOutcome.OverwrittenByCopy).Should().Be(1);

    [Fact]
    public void NeedsProcessing_ContentNotStoredYet_IsTrue()
        => new[] { FileOutcome.New, FileOutcome.ContentChanged, FileOutcome.AutoRestoredContentChanged }
            .Should().AllSatisfy(outcome => Classifications.Of(outcome).NeedsProcessing.Should().BeTrue());

    [Fact]
    public void NeedsProcessing_StoredContent_IsFalse()
        => new[] { FileOutcome.Unchanged, FileOutcome.OverwrittenByMove, FileOutcome.AutoRestored, FileOutcome.AutoRestoredMoved }
            .Should().AllSatisfy(outcome => Classifications.Of(outcome).NeedsProcessing.Should().BeFalse());

    [Fact]
    public void DatesMovAfterProcessing_ProcessedPhotoWhoseVideoTakesContent_IsTrue()
        => new[]
            {
                Classifications.Of(FileOutcome.New, FileOutcome.ContentChanged),
                Classifications.Of(FileOutcome.ContentChanged, FileOutcome.OverwrittenByMove),
                Classifications.Of(FileOutcome.AutoRestoredContentChanged, FileOutcome.AutoRestoredContentChanged),
            }
            .Should().AllSatisfy(classification => classification.DatesMovAfterProcessing.Should().BeTrue());

    [Fact]
    public void DatesMovAfterProcessing_PhotoNotProcessedOrVideoKeepingItsContent_IsFalse()
        => new[]
            {
                Classifications.Of(FileOutcome.Unchanged, FileOutcome.ContentChanged),
                Classifications.Of(FileOutcome.New, FileOutcome.Unchanged),
                Classifications.Of(FileOutcome.New),
            }
            .Should().AllSatisfy(classification => classification.DatesMovAfterProcessing.Should().BeFalse());

    [Fact]
    public void LoneLivePhotoVideo_NeedsNoProcessingAndIsNoCopy()
        => Classifications.Of(FileOutcome.LoneLivePhotoVideo)
            .Should().Match<IngestionClassification>(c => !c.NeedsProcessing && c.DuplicateCopies == 0);

    [Fact]
    public void Row_OutcomeTheStoredRowDescribesTheFileAfter_IsTheStoredRow()
        => new[]
            {
                FileOutcome.Unchanged, FileOutcome.Moved, FileOutcome.ContentChanged, FileOutcome.OverwrittenByMove,
                FileOutcome.AutoRestored, FileOutcome.AutoRestoredMoved, FileOutcome.AutoRestoredContentChanged,
            }
            .Should().AllSatisfy(outcome =>
            {
                var stored = new MediaAsset();
                new FileClassification(outcome, stored).Row.Should().BeSameAs(stored);
            });

    [Fact]
    public void Row_ContentAnotherRowKeeps_IsNone()
        => new[] { FileOutcome.DuplicateCopy, FileOutcome.OverwrittenByCopy, FileOutcome.LoneLivePhotoVideo }
            .Should().AllSatisfy(outcome => new FileClassification(outcome, new MediaAsset()).Row.Should().BeNull());

    [Fact]
    public void TakesContent_ContentTheStoredRowDoesNotHold_IsTrue()
        => new[] { FileOutcome.ContentChanged, FileOutcome.AutoRestoredContentChanged, FileOutcome.OverwrittenByMove }
            .Should().AllSatisfy(outcome => new FileClassification(outcome, new MediaAsset()).TakesContent.Should().BeTrue());

    [Fact]
    public void TakesContent_ContentTheStoredRowHolds_IsFalse()
        => new[]
            {
                FileOutcome.Unchanged, FileOutcome.Moved, FileOutcome.AutoRestored, FileOutcome.AutoRestoredMoved,
                FileOutcome.New, FileOutcome.DuplicateCopy, FileOutcome.OverwrittenByCopy,
            }
            .Should().AllSatisfy(outcome => new FileClassification(outcome, new MediaAsset()).TakesContent.Should().BeFalse());

    [Fact]
    public void RetiredFirst_NeitherHalfOverwrittenByMove_IsEmpty()
    {
        var classification = new IngestionClassification(
            new FileClassification(FileOutcome.OverwrittenByCopy, new MediaAsset(), Retired: new MediaAsset()),
            new FileClassification(FileOutcome.OverwrittenByCopy, new MediaAsset(), Retired: new MediaAsset()),
            PairingAction.None);

        classification.RetiredFirst.Should().BeEmpty();
    }

    [Fact]
    public void RetiredFirst_SingleFileOverwrittenByMove_IsItsRetiredRow()
    {
        var retired = new MediaAsset();
        var classification = new IngestionClassification(
            new FileClassification(FileOutcome.OverwrittenByMove, new MediaAsset(), Retired: retired), null, PairingAction.None);

        classification.RetiredFirst.Should().Equal(retired);
    }

    [Fact]
    public void RetiredFirst_VideoOverwrittenByMove_IsEveryHalfsRetiredRow()
    {
        var (photoRetired, videoRetired) = (new MediaAsset(), new MediaAsset());
        var classification = new IngestionClassification(
            new FileClassification(FileOutcome.OverwrittenByCopy, new MediaAsset(), Retired: photoRetired),
            new FileClassification(FileOutcome.OverwrittenByMove, new MediaAsset(), Retired: videoRetired),
            PairingAction.None);

        classification.RetiredFirst.Should().Equal(photoRetired, videoRetired);
    }

    [Fact]
    public void RetiredFirst_HalfWithoutARetiredRow_IsLeftOut()
    {
        var retired = new MediaAsset();
        var classification = new IngestionClassification(
            new FileClassification(FileOutcome.OverwrittenByMove, new MediaAsset(), Retired: retired),
            new FileClassification(FileOutcome.Moved, new MediaAsset()),
            PairingAction.None);

        classification.RetiredFirst.Should().Equal(retired);
    }
}
