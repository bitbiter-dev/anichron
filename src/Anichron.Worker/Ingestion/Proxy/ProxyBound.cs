using SixLabors.ImageSharp;

namespace Anichron.Worker.Ingestion.Proxy;

public enum ProxyEdge
{
    LongEdge = 0,
    ShortEdge = 1,
}

internal readonly record struct ProxyBound(ProxyEdge Edge, int Pixels)
{
    public Size FitTo(Size source)
    {
        // Stryker disable once Equality
        var widthIsLongEdge = source.Width >= source.Height;
        var boundedIsWidth = Edge == ProxyEdge.LongEdge == widthIsLongEdge;
        var (bounded, other) = boundedIsWidth
            ? (source.Width, source.Height)
            : (source.Height, source.Width);
        // Stryker disable once Equality
        if (bounded <= Pixels)
            return source;

        var scaledOther = Math.Max(1, (int)Math.Round((double)other * Pixels / bounded, MidpointRounding.AwayFromZero));
        return boundedIsWidth ? new Size(Pixels, scaledOther) : new Size(scaledOther, Pixels);
    }
}
