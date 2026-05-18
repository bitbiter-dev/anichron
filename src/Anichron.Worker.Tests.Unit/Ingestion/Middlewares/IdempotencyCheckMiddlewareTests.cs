using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class IdempotencyCheckMiddlewareTests
{
    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();

        public IdempotencyCheckMiddleware Build()
            => new(Repository, Substitute.For<ILogger<IdempotencyCheckMiddleware>>());
    }

    private static IngestionContext MakeContext(string? contentHash = null) => new()
    {
        Item = new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        ContentHash = contentHash,
    };

    // ==========================================================================
    // CanInvoke
    // ==========================================================================

    [Fact]
    public void CanInvoke_WhenHashSet_ReturnsTrue()
    {
        new TestFixture().Build().CanInvoke(MakeContext("abc123")).Should().BeTrue();
    }

    [Fact]
    public void CanInvoke_WhenHashNull_ReturnsFalse()
    {
        new TestFixture().Build().CanInvoke(MakeContext(null)).Should().BeFalse();
    }

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_KnownHash_DoesNotCallNextAsync()
    {
        var fx = new TestFixture();
        fx.Repository.FindByHashAsync("abc123", Arg.Any<CancellationToken>())
            .Returns(new MediaAsset());
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fx.Build().InvokeAsync(MakeContext("abc123"), NextAsync, CancellationToken.None);

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnknownHash_CallsNextAsync()
    {
        var fx = new TestFixture();
        fx.Repository.FindByHashAsync("abc123", Arg.Any<CancellationToken>())
            .Returns((MediaAsset?)null);
        var nextCalled = false;
        Task NextAsync(IngestionContext ctx, CancellationToken ct)
        {
            _ = (ctx, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await fx.Build().InvokeAsync(MakeContext("abc123"), NextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }
}
