using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Maintenance;

public sealed record TokenCleanupResult(int Deleted);

public sealed partial class TokenCleanupHandler(
    IRefreshTokenRepository repository,
    IClock clock,
    ILogger<TokenCleanupHandler> logger) : IWorkHandler<TokenCleanupPayload>
{
    public async Task<object?> RunAsync(TokenCleanupPayload payload, WorkRun run, CancellationToken ct)
    {
        var deleted = await repository.DeleteExpiredAsync(clock.GetCurrentInstant(), ct);
        Log.TokensDeleted(logger, deleted);
        return new TokenCleanupResult(deleted);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired refresh token(s).")]
        public static partial void TokensDeleted(ILogger logger, int count);
    }
}

public sealed class TokenCleanupSchedule(IOptions<WorkerSettings> options)
    : WorkSchedule<TokenCleanupPayload>(new TokenCleanupPayload(), Duration.FromHours(options.Value.TokenCleanupIntervalHours));
