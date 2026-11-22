using Anichron.Core.Data.Repository;
using NodaTime.Text;
using System.Buffers.Text;
using System.Text;

namespace Anichron.API.Endpoints;

internal static class HiddenCursors
{
    private const char separator = ' ';

    internal static string Format(Instant hiddenAt) => InstantPattern.ExtendedIso.Format(hiddenAt);

    internal static string Format(AssetView last)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{Format(last.HiddenAt!.Value)}{separator}{last.Id:N}"));

    internal static HiddenCursor? Parse(string text)
    {
        if (!Base64Url.IsValid(text))
            return null;

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text)).Split(separator);
        return parts is [var hiddenAt, var id]
            && InstantPattern.ExtendedIso.Parse(hiddenAt) is { Success: true } parsed
            && Guid.TryParseExact(id, "N", out var assetId)
                ? new HiddenCursor(parsed.Value, assetId)
                : null;
    }
}
