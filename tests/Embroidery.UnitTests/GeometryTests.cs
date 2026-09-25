using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.Geometry.Svg;

namespace Embroidery.UnitTests;

public class GeometryTests
{
    private static Region Square(double size, double x = 0, double y = 0) =>
        new([new Vec2[] { new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size) }]);

    [Fact]
    public void Cubic_flattening_stays_within_tolerance()
    {
        var p0 = new Vec2(0, 0);
        var p1 = new Vec2(10, 30);
        var p2 = new Vec2(40, -20);
        var p3 = new Vec2(50, 10);
        var pts = new List<Vec2> { p0 };
        CurveFlattener.Cubic(p0, p1, p2, p3, 0.02, pts);

        Vec2 Bezier(double t)
        {
            var u = 1 - t;
            return p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t);
        }

        // Every point on the true curve must be close to the polyline.
        for (var i = 0; i <= 500; i++)
        {
            var c = Bezier(i / 500.0);
            var best = double.MaxValue;
            for (var k = 1; k < pts.Count; k++) best = Math.Min(best, DistanceToSegment(c, pts[k - 1], pts[k]));
            Assert.True(best <= 0.021, $"deviation {best} at t={i / 500.0}");
        }

        Assert.Equal(p3, pts[^1]);
    }

    [Fact]
    public void Svg_circle_arcs_lie_on_the_circle()
    {
        var subpaths = SvgPathParser.Parse("M 0 10 A 10 10 0 1 0 20 10 A 10 10 0 1 0 0 10 Z", Matrix2D.Identity, 0.01);
        var sp = Assert.Single(subpaths);
        Assert.True(sp.Closed);
        foreach (var p in sp.Points)
        {
            Assert.InRange(Vec2.Distance(p, new Vec2(10, 10)), 9.98, 10.02);
        }
    }

    [Fact]
    public void Path_parser_handles_relative_commands_implicit_lineto_and_compact_arc_flags()
    {
        var sp = Assert.Single(SvgPathParser.Parse("m1,1 2,0 0,2 h-2z", Matrix2D.Identity));
        Assert.Equal(new[] { new Vec2(1, 1), new Vec2(3, 1), new Vec2(3, 3), new Vec2(1, 3) }, sp.Points);

        // "a1 1 0 00 1 1": flags without separators.
        var arc = Assert.Single(SvgPathParser.Parse("M0 0a1 1 0 00 1 1", Matrix2D.Identity));
        Assert.True(arc.Points[^1].ApproximatelyEquals(new Vec2(1, 1), 1e-9));

        var exp = Assert.Single(SvgPathParser.Parse("M0,0L1e1-5e-1", Matrix2D.Identity));
        Assert.Equal(new Vec2(10, -0.5), exp.Points[^1]);
    }

    [Fact]
    public void Path_parser_smooth_cubic_reflects_previous_control_point()
    {
        // With reflection the S segment mirrors the first curve, so the midpoint of the whole
        // symmetric shape lies exactly at x=20.
        var sp = Assert.Single(SvgPathParser.Parse("M0,0 C0,10 10,10 10,0 S20,-10 20,0", Matrix2D.Identity, 0.001));
        var minY = sp.Points.Min(p => p.Y);
        var maxY = sp.Points.Max(p => p.Y);
        Assert.InRange(maxY, 7.4, 7.6);
        Assert.InRange(minY, -7.6, -7.4);
    }

    [Fact]
    public void Invalid_path_data_throws_a_format_exception()
    {
        Assert.Throws<SvgPathFormatException>(() => SvgPathParser.Parse("10 10 L 20 20", Matrix2D.Identity));
        Assert.Throws<SvgPathFormatException>(() => SvgPathParser.Parse("M 10 L 20 20", Matrix2D.Identity));
    }

    [Fact]
    public void Arc_length_path_samples_by_distance()
    {
        var path = new ArcLengthPath([new(0, 0), new(10, 0), new(10, 10)]);
        Assert.Equal(20, path.Length, 9);
        Assert.Equal(new Vec2(5, 0), path.PointAt(5));
        Assert.Equal(new Vec2(10, 5), path.PointAt(15));
        Assert.Equal(new Vec2(10, 10), path.PointAtFraction(1.5));
    }

    [Theory]
    [InlineData(FillRule.EvenOdd)]
    [InlineData(FillRule.NonZero)]
    public void Scanline_respects_holes(FillRule rule)
    {
        // Outer square clockwise on screen, hole counter-clockwise: a hole under both rules.
        var region = new Region(
        [
            new Vec2[] { new(0, 0), new(30, 0), new(30, 30), new(0, 30) },
            new Vec2[] { new(10, 10), new(10, 20), new(20, 20), new(20, 10) },
        ], rule);

        var through = PolygonOps.Scanline(region, 15);
        Assert.Equal(2, through.Count);
        Assert.Equal(0, through[0].X0, 9);
        Assert.Equal(10, through[0].X1, 9);
        Assert.Equal(20, through[1].X0, 9);
        Assert.Equal(30, through[1].X1, 9);

        Assert.Single(PolygonOps.Scanline(region, 5));
        Assert.False(PolygonOps.Contains(region, new Vec2(15, 15)));
        Assert.True(PolygonOps.Contains(region, new Vec2(5, 15)));
    }

    [Fact]
    public void Offset_insets_a_square()
    {
        var inset = PolygonOps.Offset(Square(10), -1);
        var b = inset.Bounds;
        Assert.Equal(1, b.MinX, 2);
        Assert.Equal(9, b.MaxX, 2);
        Assert.Equal(64, PolygonOps.Area(inset), 1);
    }

    [Fact]
    public void Offset_that_consumes_the_shape_returns_empty_region()
    {
        Assert.Empty(PolygonOps.Offset(Square(2), -2).Rings);
    }

    private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared < 1e-18 ? 0 : Math.Clamp(Vec2.Dot(p - a, ab) / ab.LengthSquared, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }
}
