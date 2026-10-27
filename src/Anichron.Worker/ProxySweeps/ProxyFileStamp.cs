using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.ProxySweeps;

internal readonly record struct ProxyFileStamp(long SizeBytes, Instant LastWriteTime)
{
    /// <summary>The file's size and last write time, the time truncated to the microseconds PostgreSQL keeps.</summary>
    /// <param name="file">The file as it is now.</param>
    public static ProxyFileStamp Of(IFileInfo file)
    {
        var ticks = Instant.FromDateTimeUtc(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)).ToUnixTimeTicks();
        return new ProxyFileStamp(file.Length, Instant.FromUnixTimeTicks(ticks - (ticks % TimeSpan.TicksPerMicrosecond)));
    }
}
