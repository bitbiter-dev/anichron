using Anichron.Core.Domain;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion;

internal sealed record LivePhotoLinkResult(
    IReadOnlyList<IngestionItem> Items,
    IReadOnlySet<string> ClaimedPaths);

internal interface ILivePhotoLinker
{
    LivePhotoLinkResult Link(IReadOnlyList<string> filesInDirectory, string rootPath);
}

internal sealed class LivePhotoLinker(IFileSystem fileSystem) : ILivePhotoLinker
{
    public LivePhotoLinkResult Link(IReadOnlyList<string> filesInDirectory, string rootPath)
    {
        var heicFiles = new List<string>();
        var movFilesByBaseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in filesInDirectory)
        {
            var ext = fileSystem.Path.GetExtension(path);
            if (ext.Equals(MediaFileExtensions.Heic, StringComparison.OrdinalIgnoreCase))
                heicFiles.Add(path);
            else if (ext.Equals(MediaFileExtensions.Mov, StringComparison.OrdinalIgnoreCase))
                movFilesByBaseName[fileSystem.Path.GetFileNameWithoutExtension(path)] = path;
        }

        var items = new List<IngestionItem>(heicFiles.Count);
        var claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var heicPath in heicFiles)
        {
            claimedPaths.Add(heicPath);
            var baseName = fileSystem.Path.GetFileNameWithoutExtension(heicPath);
            var heicRelativePath = fileSystem.Path.GetRelativePath(rootPath, heicPath);

            if (movFilesByBaseName.TryGetValue(baseName, out var movPath))
            {
                var movRelativePath = fileSystem.Path.GetRelativePath(rootPath, movPath);
                items.Add(new LivePhotoPairItem(heicPath, heicRelativePath, movPath, movRelativePath));
                claimedPaths.Add(movPath);
            }
            else
            {
                items.Add(new SingleFileItem(heicPath, heicRelativePath, MediaType.Image));
            }
        }

        return new LivePhotoLinkResult(items, claimedPaths);
    }
}
