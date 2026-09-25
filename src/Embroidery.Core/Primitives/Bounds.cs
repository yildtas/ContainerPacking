namespace Embroidery.Core.Primitives;

/// <summary>Axis-aligned bounding box in millimetres.</summary>
public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly Bounds Empty = new(double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Vec2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public Bounds Include(Vec2 p) => new(
        Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Bounds Include(Bounds other) => other.IsEmpty ? this : IsEmpty ? other : new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public static Bounds Of(IEnumerable<Vec2> points)
    {
        var b = Empty;
        foreach (var p in points) b = b.Include(p);
        return b;
    }
}
