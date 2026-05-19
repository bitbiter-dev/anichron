using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class LoggingMiddlewareTests
{
    private static IngestionContext MakeContext(string absolutePath = "/abs/photo.jpg") => new()
    {
        Item = new SingleFileItem(absolutePath, "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
    };

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Always_CallsNextAsync()
    {
        var middleware = new LoggingMiddleware(Substitute.For<ILogger<LoggingMiddleware>>());
        var nextCalled = false;
        Task nextAsync(IngestionContext context, CancellationToken ct)
        {
            _ = (context, ct);
            nextCalled = true;
            return Task.CompletedTask;
        }

        await middleware.InvokeAsync(MakeContext(), nextAsync, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_ExceptionPropagatesAsync()
    {
        var middleware = new LoggingMiddleware(Substitute.For<ILogger<LoggingMiddleware>>());
        static Task nextAsync(IngestionContext context, CancellationToken ct)
        {
            _ = (context, ct);
            throw new InvalidOperationException("downstream error");
        }

        var act = async () => await middleware.InvokeAsync(MakeContext(), nextAsync, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("downstream error");
    }

    // ==========================================================================
    // Log output
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_Always_LogsStartMessageContainingPathAsync()
    {
        var logger = new CapturingLogger();
        var middleware = new LoggingMiddleware(logger);
        static Task nextAsync(IngestionContext context, CancellationToken ct)
        {
            _ = (context, ct);
            return Task.CompletedTask;
        }

        await middleware.InvokeAsync(MakeContext("/abs/photo.jpg"), nextAsync, CancellationToken.None);

        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Debug && entry.Message.Contains("/abs/photo.jpg"));
    }

    [Fact]
    public async Task InvokeAsync_WhenNextCompletes_LogsCompletionMessageAsync()
    {
        var logger = new CapturingLogger();
        var middleware = new LoggingMiddleware(logger);
        static Task nextAsync(IngestionContext context, CancellationToken ct)
        {
            _ = (context, ct);
            return Task.CompletedTask;
        }

        await middleware.InvokeAsync(MakeContext("/abs/photo.jpg"), nextAsync, CancellationToken.None);

        logger.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_OnlyLogsStartMessageAsync()
    {
        var logger = new CapturingLogger();
        var middleware = new LoggingMiddleware(logger);
        static Task nextAsync(IngestionContext context, CancellationToken ct)
        {
            _ = (context, ct);
            throw new InvalidOperationException();
        }

        try
        { await middleware.InvokeAsync(MakeContext(), nextAsync, CancellationToken.None); }
        catch (InvalidOperationException exception) { _ = exception; }

        logger.Entries.Should().ContainSingle()
            .Which.Message.Should().Contain("Ingesting");
    }

    private sealed class CapturingLogger : ILogger<LoggingMiddleware>
    {
        private readonly List<(LogLevel Level, string Message)> entries = [];
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => entries;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
