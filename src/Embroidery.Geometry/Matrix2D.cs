using Embroidery.Core.Primitives;

namespace Embroidery.Geometry;

/// <summary>
/// Affine transform in SVG convention: [a c e; b d f; 0 0 1].
/// A point is mapped as x' = a*x + c*y + e, y' = b*x + d*y + f.
/// </summary>
public readonly record struct Matrix2D(double A, double B, double C, double D, double E, double F)
{
    public static readonly Matrix2D Identity = new(1, 0, 0, 1, 0, 0);

    public static Matrix2D Translate(double tx, double ty) => new(1, 0, 0, 1, tx, ty);
    public static Matrix2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Matrix2D Rotate(double degrees)
    {
        var r = degrees * Math.PI / 180;
        var c = Math.Cos(r);
        var s = Math.Sin(r);
        return new(c, s, -s, c, 0, 0);
    }

    public static Matrix2D SkewX(double degrees) => new(1, 0, Math.Tan(degrees * Math.PI / 180), 1, 0, 0);
    public static Matrix2D SkewY(double degrees) => new(1, Math.Tan(degrees * Math.PI / 180), 0, 1, 0, 0);

    /// <summary>Returns the transform that applies <paramref name="inner"/> first and then this one.</summary>
    public Matrix2D Multiply(Matrix2D inner) => new(
        A * inner.A + C * inner.B,
        B * inner.A + D * inner.B,
        A * inner.C + C * inner.D,
        B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E,
        B * inner.E + D * inner.F + F);

    public Vec2 Apply(Vec2 p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    /// <summary>Geometric mean scale factor, used to convert stroke widths.</summary>
    public double AverageScale => Math.Sqrt(Math.Abs(A * D - B * C));
}
