using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion;

internal sealed record SecondaryFileDescriptor(
    string AbsolutePath,
    string RelativePath,
    MediaType MediaType);

internal abstract record IngestionItem(string AbsolutePath, string RelativePath)
{
    public abstract MediaType PrimaryMediaType { get; }
    public abstract SecondaryFileDescriptor? SecondaryFile { get; }
}

internal sealed record SingleFileItem(
    string AbsolutePath,
    string RelativePath,
    MediaType MediaType) : IngestionItem(AbsolutePath, RelativePath)
{
    public override MediaType PrimaryMediaType => MediaType;
    public override SecondaryFileDescriptor? SecondaryFile => null;
}

internal sealed record LivePhotoPairItem(
    string AbsolutePath,
    string RelativePath,
    string MovAbsolutePath,
    string MovRelativePath) : IngestionItem(AbsolutePath, RelativePath)
{
    public override MediaType PrimaryMediaType => MediaType.LivePhoto;
    public override SecondaryFileDescriptor? SecondaryFile => new(MovAbsolutePath, MovRelativePath, MediaType.Video);
}
