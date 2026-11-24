using Anichron.Core.Data.Repository;
using NodaTime.Text;
using System.Buffers.Text;
using System.Text;

namespace Anichron.API.Endpoints;

internal static class AssetCursors
{
    private const char separator = ' ';

    internal static string FormatInstant(Instant at) => InstantPattern.ExtendedIso.Format(at);

    internal static string Format(AssetCursor last)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{FormatInstant(last.At)}{separator}{last.AssetId:N}"));

    internal static AssetCursor? Parse(string text)
    {
        if (!Base64Url.IsValid(text))
            return null;

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text)).Split(separator);
        return parts is [var at, var id]
            && InstantPattern.ExtendedIso.Parse(at) is { Success: true } parsed
            && Guid.TryParseExact(id, "N", out var assetId)
                ? new AssetCursor(parsed.Value, assetId)
                : null;
    }
}
