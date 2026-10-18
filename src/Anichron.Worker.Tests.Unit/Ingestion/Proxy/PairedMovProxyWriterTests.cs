using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Proxy;

public sealed class PairedMovProxyWriterTests
{
    private static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MovId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private sealed class TestFixture
    {
        public IVideoProxyGenerator Generator { get; } = Substitute.For<IVideoProxyGenerator>();
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IProxyDirectoryStrategy DirectoryStrategy { get; } = Substitute.For<IProxyDirectoryStrategy>();
        public MockFileSystem FileSystem { get; } = new();
        public List<string> TranscodedSources { get; } = [];

        public TestFixture()
        {
            Generator.FileName.Returns("video_720p.mp4");
            Generator.ProxyType.Returns(ProxyType.WebVideo);
            DirectoryStrategy.GetDirectory(ConfigId, "cafebabe").Returns("ca/mov");
            Generator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    TranscodedSources.Add(callInfo.ArgAt<string>(0));
                    return FileSystem.File.WriteAllBytesAsync(callInfo.ArgAt<string>(1), [0xAA, 0xBB]);
                });
        }

        public PairedMovProxyWriter Build()
            => new([Generator],
                   DirectoryStrategy,
                   new ProxyStagingWriter(
                       FileSystem,
                       Substitute.For<IClock>(),
                       Substitute.For<IGuidFactory>(),
                       Substitute.For<ILogger<ProxyStagingWriter>>()),
                   Repository,
                   Substitute.For<ILogger<PairedMovProxyWriter>>());
    }

    private static IngestionContext MakePairContext() => new()
    {
        Item = new LivePhotoPairItem("/nas/photo.heic", "photo.heic", "/nas/photo.mov", "photo.mov"),
        Config = new UserStorageConfig { Id = ConfigId, RootPath = "/nas" },
        AttemptId = Guid.NewGuid(),
        AssetId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        ContentHash = "deadbeef",
        SecondaryHash = "cafebabe",
    };

    private static MediaAsset Mov(params ProxyType[] existing) => new()
    {
        Id = MovId,
        ContentHash = "cafebabe",
        MediaType = MediaType.Video,
        ProxyFiles = [.. existing.Select(type => new ProxyFile { AssetId = MovId, ProxyType = type })],
    };

    [Fact]
    public async Task WriteMissingAsync_MovWithoutVideoProxy_TranscodesTheMovIntoItsOwnDirectoryAsync()
    {
        var fixture = new TestFixture();

        var wrote = await fixture.Build().WriteMissingAsync(MakePairContext(), Mov(), CancellationToken.None);

        wrote.Should().BeTrue();
        fixture.TranscodedSources.Should().Equal("/nas/photo.mov");
        fixture.FileSystem.FileExists("/data/proxies/ca/mov/video_720p.mp4").Should().BeTrue();
    }

    [Fact]
    public async Task WriteMissingAsync_MovWithoutVideoProxy_AddsARowOwnedByTheMovAsync()
    {
        var fixture = new TestFixture();
        var context = MakePairContext();
        var added = new List<ProxyFile>();
        fixture.Repository.When(r => r.AddProxyFile(Arg.Any<ProxyFile>())).Do(call => added.Add(call.Arg<ProxyFile>()));

        await fixture.Build().WriteMissingAsync(context, Mov(), CancellationToken.None);

        var row = added.Should().ContainSingle().Which;
        row.AssetId.Should().Be(MovId);
        row.ProxyType.Should().Be(ProxyType.WebVideo);
        row.ProxyPath.Should().Be("ca/mov/video_720p.mp4");
        context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(row);
    }

    [Fact]
    public async Task WriteMissingAsync_LeavesTheHeicsOwnProxiesToTheHeicAsync()
    {
        var fixture = new TestFixture();
        var context = MakePairContext();
        context.AddProxyFile(new ProxyFile { AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail });

        await fixture.Build().WriteMissingAsync(context, Mov(), CancellationToken.None);

        fixture.Repository.Received(1).AddProxyFile(Arg.Any<ProxyFile>());
        fixture.Repository.DidNotReceive().AddProxyFile(Arg.Is<ProxyFile>(p => p.AssetId == context.AssetId));
    }

    [Fact]
    public async Task WriteMissingAsync_MovThatAlreadyHasItsVideoProxy_TranscodesNothingAsync()
    {
        var fixture = new TestFixture();
        var context = MakePairContext();

        var wrote = await fixture.Build().WriteMissingAsync(context, Mov(ProxyType.WebVideo), CancellationToken.None);

        wrote.Should().BeFalse();
        fixture.TranscodedSources.Should().BeEmpty();
        fixture.Repository.DidNotReceive().AddProxyFile(Arg.Any<ProxyFile>());
        context.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteMissingAsync_MovWithOnlyOtherProxyTypes_StillGetsItsVideoProxyAsync()
    {
        var fixture = new TestFixture();

        var wrote = await fixture.Build().WriteMissingAsync(
            MakePairContext(), Mov(ProxyType.Thumbnail), CancellationToken.None);

        wrote.Should().BeTrue();
        fixture.TranscodedSources.Should().ContainSingle();
    }
}
