namespace Embroidery.Core.Primitives;

public enum FillRule
{
    NonZero,
    EvenOdd,
}

/// <summary>
/// A fillable area described by closed rings. Which rings are holes is decided by
/// <see cref="FillRule"/>, exactly like SVG, so imported artwork keeps its meaning.
/// Rings are stored without repeating the first point at the end.
/// </summary>
public sealed record Region(IReadOnlyList<IReadOnlyList<Vec2>> Rings, FillRule FillRule = FillRule.EvenOdd)
{
    public Bounds Bounds
    {
        get
        {
            var b = Bounds.Empty;
            foreach (var ring in Rings) b = b.Include(Bounds.Of(ring));
            return b;
        }
    }
}
