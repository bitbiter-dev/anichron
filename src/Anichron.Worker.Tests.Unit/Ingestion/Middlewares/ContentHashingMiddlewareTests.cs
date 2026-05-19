using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class ContentHashingMiddlewareTests
{
    private static IngestionContext MakeContext(string path = "/abs/photo.jpg") => new()
    {
        Item = new SingleFileItem(path, "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
    };

    // ==========================================================================
    // CanInvoke
    // ==========================================================================

    [Fact]
    public void CanInvoke_Always_ReturnsTrue()
    {
        var middleware = new ContentHashingMiddleware(new MockFileSystem());
        middleware.CanInvoke(MakeContext()).Should().BeTrue();
    }

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Always_CallsNextAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData([0x01, 0x02, 0x03]),
        });
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await new ContentHashingMiddleware(fs).InvokeAsync(MakeContext("/abs/photo.jpg"), NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Always_SetsNonEmptyHashAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.jpg"] = new MockFileData([0x01, 0x02, 0x03]),
        });
        var context = MakeContext("/abs/photo.jpg");

        await new ContentHashingMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_SameContent_ProducesSameHashAsync()
    {
        byte[] content = [0xAA, 0xBB, 0xCC];
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/a.jpg"] = new MockFileData(content),
            ["/abs/b.jpg"] = new MockFileData(content),
        });
        var contextA = new IngestionContext
        {
            Item = new SingleFileItem("/abs/a.jpg", "a.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        };
        var contextB = new IngestionContext
        {
            Item = new SingleFileItem("/abs/b.jpg", "b.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        };

        await new ContentHashingMiddleware(fs).InvokeAsync(contextA, (_, _) => Task.CompletedTask, CancellationToken.None);
        await new ContentHashingMiddleware(fs).InvokeAsync(contextB, (_, _) => Task.CompletedTask, CancellationToken.None);

        contextA.ContentHash.Should().Be(contextB.ContentHash);
    }

    [Fact]
    public async Task InvokeAsync_DifferentContent_ProducesDifferentHashAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/a.jpg"] = new MockFileData([0x01, 0x02]),
            ["/abs/b.jpg"] = new MockFileData([0x03, 0x04]),
        });
        var contextA = new IngestionContext
        {
            Item = new SingleFileItem("/abs/a.jpg", "a.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        };
        var contextB = new IngestionContext
        {
            Item = new SingleFileItem("/abs/b.jpg", "b.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        };

        await new ContentHashingMiddleware(fs).InvokeAsync(contextA, (_, _) => Task.CompletedTask, CancellationToken.None);
        await new ContentHashingMiddleware(fs).InvokeAsync(contextB, (_, _) => Task.CompletedTask, CancellationToken.None);

        contextA.ContentHash.Should().NotBe(contextB.ContentHash);
    }

    [Fact]
    public async Task InvokeAsync_LivePhotoPairItem_SetsMovContentHashAsync()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/abs/photo.heic"] = new MockFileData([0x01, 0x02]),
            ["/abs/photo.mov"] = new MockFileData([0x03, 0x04]),
        });
        var context = new IngestionContext
        {
            Item = new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        };

        await new ContentHashingMiddleware(fs).InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.MovContentHash.Should().NotBeNullOrEmpty();
        context.MovContentHash.Should().NotBe(context.ContentHash);
    }
}
