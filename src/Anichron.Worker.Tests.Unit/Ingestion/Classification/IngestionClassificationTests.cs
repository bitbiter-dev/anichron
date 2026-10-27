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
        => (DuplicateCopies(FileOutcome.Moved), DuplicateCopies(FileOutcome.PathTaken, FileOutcome.Unchanged))
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
    public void LoneLivePhotoVideo_NeedsNoProcessingAndIsNoCopy()
        => Classifications.Of(FileOutcome.LoneLivePhotoVideo)
            .Should().Match<IngestionClassification>(c => !c.NeedsProcessing && c.DuplicateCopies == 0);
}
