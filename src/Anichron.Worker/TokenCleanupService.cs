using Anichron.Core.Data.Repository;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker;

public sealed partial class TokenCleanupService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<WorkerSettings> options,
    ILogger<TokenCleanupService> logger) : BackgroundService
{
    private readonly WorkerSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(_settings.TokenCleanupIntervalHours), stoppingToken);
        }
    }

    internal async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var now = clock.GetCurrentInstant();
        var deleted = await repo.DeleteExpiredAsync(now, ct);
        Log.TokensDeleted(logger, deleted);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired refresh token(s).")]
        public static partial void TokensDeleted(ILogger logger, int count);
    }
}
