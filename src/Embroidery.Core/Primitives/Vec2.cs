namespace Embroidery.Core.Primitives;

/// <summary>
/// A 2D point or vector in design space. Units are always millimetres;
/// X grows to the right and Y grows downwards (same as SVG).
/// </summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Unit vector in the same direction, or zero for a zero-length vector.</summary>
    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-12 ? Zero : new Vec2(X / len, Y / len);
    }

    /// <summary>Perpendicular vector rotated +90° (in a Y-down system this points to the right-hand side of travel).</summary>
    public Vec2 Perpendicular => new(-Y, X);

    public Vec2 Rotate(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public bool ApproximatelyEquals(Vec2 other, double epsilon = 1e-9) =>
        Math.Abs(X - other.X) <= epsilon && Math.Abs(Y - other.Y) <= epsilon;

    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###})");
}
