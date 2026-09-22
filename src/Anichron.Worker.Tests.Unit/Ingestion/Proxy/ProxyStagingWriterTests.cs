using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Anichron.Worker.Tests.Unit.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class ProxyStagingWriterTests
{
    private sealed class TestFixture
    {
        public MockFileSystem FileSystem { get; init; } = new();
        public Instant Now { get; } = Instant.FromUtc(2026, 9, 21, 8, 0, 0);
        public IClock Clock { get; }
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();

        public TestFixture()
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            Clock = clock;
        }

        public ProxyStagingWriter Build()
            => new(FileSystem,
                   Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
                   Clock,
                   GuidFactory,
                   Substitute.For<ILogger<ProxyStagingWriter>>());
    }

    private static IngestionContext MakeContext() => new()
    {
        Item = new SingleFileItem("/nas/photo.jpg", "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
        AssetId = Guid.NewGuid(),
        ContentHash = "0123456789abcdef",
    };

    private static Func<string, CancellationToken, Task> Writing(IFileSystem fileSystem, byte[] bytes)
        => (temporaryPath, token) => fileSystem.File.WriteAllBytesAsync(temporaryPath, bytes, token);

    // ==========================================================================
    // TemporaryPathFor
    // ==========================================================================

    [Fact]
    public void TemporaryPathFor_AppendsTheTemporarySuffix()
    {
        ProxyStagingWriter.TemporaryPathFor("/proxies/ab/cd/thumbnail.jpg")
            .Should().Be("/proxies/ab/cd/thumbnail.jpg.tmp");
    }

    // ==========================================================================
    // WriteAsync
    // ==========================================================================

    [Fact]
    public async Task WriteAsync_HandsTheProducerATemporaryPathAsync()
    {
        var fixture = new TestFixture();
        var producedPath = string.Empty;

        await fixture.Build().WriteAsync(
            MakeContext(),
            "ab/cd/thumbnail.jpg",
            ProxyType.Thumbnail,
            (temporaryPath, token) =>
            {
                producedPath = temporaryPath;
                return fixture.FileSystem.File.WriteAllBytesAsync(temporaryPath, [0x01], token);
            },
            CancellationToken.None);

        producedPath.Should().Be("/proxies/ab/cd/thumbnail.jpg.tmp");
    }

    [Fact]
    public async Task WriteAsync_RenamesTheCompletedFileIntoPlaceAsync()
    {
        var fixture = new TestFixture();

        await fixture.Build().WriteAsync(
            MakeContext(), "ab/cd/thumbnail.jpg", ProxyType.Thumbnail,
            Writing(fixture.FileSystem, [0x01, 0x02]), CancellationToken.None);

        fixture.FileSystem.FileExists("/proxies/ab/cd/thumbnail.jpg").Should().BeTrue();
        fixture.FileSystem.FileExists("/proxies/ab/cd/thumbnail.jpg.tmp").Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_CreatesTheProxyDirectoryAsync()
    {
        var fixture = new TestFixture();

        await fixture.Build().WriteAsync(
            MakeContext(), "ab/cd/thumbnail.jpg", ProxyType.Thumbnail,
            Writing(fixture.FileSystem, [0x01]), CancellationToken.None);

        fixture.FileSystem.Directory.Exists("/proxies/ab/cd").Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ExistingProxy_IsOverwrittenByTheRetryAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/proxies/ab/cd/thumbnail.jpg", new MockFileData([0xFF, 0xFF, 0xFF]));

        await fixture.Build().WriteAsync(
            MakeContext(), "ab/cd/thumbnail.jpg", ProxyType.Thumbnail,
            Writing(fixture.FileSystem, [0x01]), CancellationToken.None);

        var written = await fixture.FileSystem.File.ReadAllBytesAsync(
            "/proxies/ab/cd/thumbnail.jpg", CancellationToken.None);
        written.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteAsync_RegistersTheProxyOnTheContextAsync()
    {
        var fixture = new TestFixture();
        fixture.GuidFactory.NewGuid().Returns(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var context = MakeContext();

        await fixture.Build().WriteAsync(
            context, "ab/cd/thumbnail.jpg", ProxyType.Thumbnail,
            Writing(fixture.FileSystem, [0x01, 0x02, 0x03]), CancellationToken.None);

        var proxy = context.ProxyFiles.Should().ContainSingle().Subject;
        proxy.Id.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        proxy.AssetId.Should().Be(context.AssetId);
        proxy.ProxyPath.Should().Be("ab/cd/thumbnail.jpg");
        proxy.ProxyType.Should().Be(ProxyType.Thumbnail);
        proxy.SizeBytes.Should().Be(3);
        proxy.CreatedAt.Should().Be(fixture.Now);
    }

    [Fact]
    public async Task WriteAsync_ProducerThrows_PublishesNothingAsync()
    {
        var fixture = new TestFixture();
        var context = MakeContext();

        var act = () => fixture.Build().WriteAsync(
            context, "ab/cd/thumbnail.jpg", ProxyType.Thumbnail,
            async (temporaryPath, token) =>
            {
                await fixture.FileSystem.File.WriteAllBytesAsync(temporaryPath, [0x01], token);
                throw new InvalidOperationException("boom");
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        fixture.FileSystem.FileExists("/proxies/ab/cd/thumbnail.jpg").Should().BeFalse();
        fixture.FileSystem.FileExists("/proxies/ab/cd/thumbnail.jpg.tmp").Should().BeFalse();
        context.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_TemporaryFileCannotBeDeleted_StillPropagatesTheProducerExceptionAsync()
    {
        var fixture = new TestFixture
        {
            FileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/thumbnail.jpg.tmp"),
        };

        var act = () => fixture.Build().WriteAsync(
            MakeContext(), "ab/cd/thumbnail.jpg", ProxyType.Thumbnail,
            async (temporaryPath, token) =>
            {
                await fixture.FileSystem.File.WriteAllBytesAsync(temporaryPath, [0x01], token);
                throw new InvalidOperationException("boom");
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
