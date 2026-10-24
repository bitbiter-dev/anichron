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
}
