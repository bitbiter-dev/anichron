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
        var widthIsLongEdge = source.Width == Math.Max(source.Width, source.Height);
        var boundedIsWidth = Edge == ProxyEdge.LongEdge == widthIsLongEdge;
        var (bounded, other) = boundedIsWidth
            ? (source.Width, source.Height)
            : (source.Height, source.Width);
        var target = Math.Min(bounded, Pixels);

        var scaledOther = Math.Max(1, (int)Math.Round((double)other * target / bounded, MidpointRounding.AwayFromZero));
        return boundedIsWidth ? new Size(target, scaledOther) : new Size(scaledOther, target);
    }
}
