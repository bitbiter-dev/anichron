using Anichron.Core;
using Anichron.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Infrastructure.Tests.Unit.Storage;

public sealed class OriginalStoreTests
{
    [Fact]
    public void RootExists_OriginalsRootPresent_ReturnsTrue()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(StoragePath.OriginalsRoot);

        new OriginalStore(fileSystem).RootExists().Should().BeTrue();
    }

    [Fact]
    public void RootExists_OnlyTheProxyRootPresent_ReturnsFalse()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(StoragePath.ProxiesRoot);

        new OriginalStore(fileSystem).RootExists().Should().BeFalse();
    }
}
