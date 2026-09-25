using Embroidery.Core.Primitives;

namespace Embroidery.Geometry;

/// <summary>
/// Converts curves into polylines with a bounded deviation (tolerance, mm) from the true curve.
/// Output lists never include the start point; callers append from their current position.
/// </summary>
public static class CurveFlattener
{
    public const double DefaultTolerance = 0.02;
    private const int MaxDepth = 16;

    public static void Cubic(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double tolerance, List<Vec2> output)
    {
        SubdivideCubic(p0, p1, p2, p3, tolerance * tolerance, 0, output);
    }

    public static void Quadratic(Vec2 p0, Vec2 q, Vec2 p2, double tolerance, List<Vec2> output)
    {
        // Exact degree elevation to a cubic.
        var c1 = p0 + (q - p0) * (2.0 / 3.0);
        var c2 = p2 + (q - p2) * (2.0 / 3.0);
        Cubic(p0, c1, c2, p2, tolerance, output);
    }

    private static void SubdivideCubic(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double tolSq, int depth, List<Vec2> output)
    {
        if (depth >= MaxDepth || IsFlat(p0, p1, p2, p3, tolSq))
        {
            output.Add(p3);
            return;
        }

        // de Casteljau split at t = 0.5.
        var p01 = Vec2.Lerp(p0, p1, 0.5);
        var p12 = Vec2.Lerp(p1, p2, 0.5);
        var p23 = Vec2.Lerp(p2, p3, 0.5);
        var p012 = Vec2.Lerp(p01, p12, 0.5);
        var p123 = Vec2.Lerp(p12, p23, 0.5);
        var mid = Vec2.Lerp(p012, p123, 0.5);

        SubdivideCubic(p0, p01, p012, mid, tolSq, depth + 1, output);
        SubdivideCubic(mid, p123, p23, p3, tolSq, depth + 1, output);
    }

    /// <summary>
    /// Flatness test: the curve deviates from its chord by at most 3/4 of the largest
    /// control point offset, so comparing (3/4 · d)^2 against tolerance^2 is conservative.
    /// </summary>
    private static bool IsFlat(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, double tolSq)
    {
        var ux = 3 * p1.X - 2 * p0.X - p3.X;
        var uy = 3 * p1.Y - 2 * p0.Y - p3.Y;
        var vx = 3 * p2.X - 2 * p3.X - p0.X;
        var vy = 3 * p2.Y - 2 * p3.Y - p0.Y;
        var m = Math.Max(ux * ux, vx * vx) + Math.Max(uy * uy, vy * vy);
        return m <= 16 * tolSq;
    }

    /// <summary>
    /// SVG elliptical arc (endpoint parameterisation, SVG 1.1 appendix F.6) converted to cubic
    /// segments of at most 90° each and flattened. Coordinates are in the arc's own space;
    /// <paramref name="transform"/> maps generated control points into output space.
    /// </summary>
    public static void Arc(
        Vec2 from, double rx, double ry, double xAxisRotationDeg, bool largeArc, bool sweep, Vec2 to,
        Matrix2D transform, double tolerance, List<Vec2> output)
    {
        if (from.ApproximatelyEquals(to))
        {
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx < 1e-12 || ry < 1e-12)
        {
            output.Add(transform.Apply(to));
            return;
        }

        var phi = xAxisRotationDeg * Math.PI / 180;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);

        var dx = (from.X - to.X) / 2;
        var dy = (from.Y - to.Y) / 2;
        var x1p = cosPhi * dx + sinPhi * dy;
        var y1p = -sinPhi * dx + cosPhi * dy;

        // Scale radii up if they cannot span the endpoints.
        var lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lambda > 1)
        {
            var s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        var num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        var den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var coef = den < 1e-24 ? 0 : Math.Sqrt(Math.Max(0, num / den));
        if (largeArc == sweep) coef = -coef;

        var cxp = coef * (rx * y1p / ry);
        var cyp = coef * -(ry * x1p / rx);
        var cx = cosPhi * cxp - sinPhi * cyp + (from.X + to.X) / 2;
        var cy = sinPhi * cxp + cosPhi * cyp + (from.Y + to.Y) / 2;

        var theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        var step = delta / segments;
        var k = 4.0 / 3.0 * Math.Tan(step / 4);

        Vec2 Point(double t) => new(
            cx + rx * Math.Cos(t) * cosPhi - ry * Math.Sin(t) * sinPhi,
            cy + rx * Math.Cos(t) * sinPhi + ry * Math.Sin(t) * cosPhi);

        Vec2 Derivative(double t) => new(
            -rx * Math.Sin(t) * cosPhi - ry * Math.Cos(t) * sinPhi,
            -rx * Math.Sin(t) * sinPhi + ry * Math.Cos(t) * cosPhi);

        var t0 = theta1;
        var start = from;
        for (var i = 0; i < segments; i++)
        {
            var t1 = t0 + step;
            var end = i == segments - 1 ? to : Point(t1);
            var c1 = start + Derivative(t0) * k;
            var c2 = end - Derivative(t1) * k;
            Cubic(transform.Apply(start), transform.Apply(c1), transform.Apply(c2), transform.Apply(end), tolerance, output);
            start = end;
            t0 = t1;
        }
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        var dot = ux * vx + uy * vy;
        var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        var a = Math.Acos(Math.Clamp(dot / len, -1, 1));
        return ux * vy - uy * vx < 0 ? -a : a;
    }
}
