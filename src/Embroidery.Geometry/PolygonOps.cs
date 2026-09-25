using Clipper2Lib;
using FillRule = Embroidery.Core.Primitives.FillRule;
using Embroidery.Core.Primitives;

namespace Embroidery.Geometry;

/// <summary>A horizontal span [X0, X1] where a scanline is inside a region.</summary>
public readonly record struct Interval(double X0, double X1)
{
    public double Length => X1 - X0;
}

public static class PolygonOps
{
    /// <summary>Signed area; positive for counter-clockwise rings in a Y-up system (clockwise on screen).</summary>
    public static double SignedArea(IReadOnlyList<Vec2> ring)
    {
        double sum = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            sum += Vec2.Cross(ring[j], ring[i]);
        }

        return sum / 2;
    }

    public static double Perimeter(IReadOnlyList<Vec2> ring)
    {
        double sum = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            sum += Vec2.Distance(ring[j], ring[i]);
        }

        return sum;
    }

    /// <summary>Area of a region honouring its fill rule.</summary>
    public static double Area(Region region) => Math.Abs(Clipper.Area(ToClipper(Normalize(region))));

    public static bool Contains(Region region, Vec2 p)
    {
        var winding = 0;
        var crossings = 0;
        foreach (var ring in region.Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                if ((a.Y <= p.Y) == (b.Y <= p.Y)) continue;
                var x = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                if (x > p.X)
                {
                    crossings++;
                    winding += b.Y > a.Y ? 1 : -1;
                }
            }
        }

        return region.FillRule == FillRule.EvenOdd ? (crossings & 1) == 1 : winding != 0;
    }

    /// <summary>True when every sample along the segment lies inside the region.</summary>
    public static bool SegmentInside(Region region, Vec2 a, Vec2 b, double sampleStepMm = 0.25)
    {
        var len = Vec2.Distance(a, b);
        var n = Math.Max(2, (int)Math.Ceiling(len / sampleStepMm));
        for (var i = 1; i < n; i++)
        {
            if (!Contains(region, Vec2.Lerp(a, b, (double)i / n))) return false;
        }

        return true;
    }

    /// <summary>
    /// Inside spans of the horizontal line at <paramref name="y"/>, sorted by X, honouring the fill rule.
    /// </summary>
    public static List<Interval> Scanline(Region region, double y)
    {
        var hits = new List<(double X, int Dir)>();
        foreach (var ring in region.Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[j];
                var b = ring[i];
                // Half-open rule avoids double counting at vertices.
                if ((a.Y <= y) == (b.Y <= y)) continue;
                var x = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                hits.Add((x, b.Y > a.Y ? 1 : -1));
            }
        }

        hits.Sort((l, r) => l.X.CompareTo(r.X));
        var result = new List<Interval>();
        var winding = 0;
        var count = 0;
        double start = 0;
        foreach (var (x, dir) in hits)
        {
            var wasInside = region.FillRule == FillRule.EvenOdd ? (count & 1) == 1 : winding != 0;
            count++;
            winding += dir;
            var isInside = region.FillRule == FillRule.EvenOdd ? (count & 1) == 1 : winding != 0;
            if (!wasInside && isInside) start = x;
            else if (wasInside && !isInside && x - start > 1e-9) result.Add(new Interval(start, x));
        }

        return result;
    }

    public static Region Transform(Region region, Func<Vec2, Vec2> map) =>
        region with { Rings = region.Rings.Select(r => (IReadOnlyList<Vec2>)r.Select(map).ToArray()).ToArray() };

    /// <summary>
    /// Resolves the fill rule into simple non-overlapping rings (outer rings + holes, even-odd safe).
    /// </summary>
    public static Region Normalize(Region region)
    {
        var paths = ToClipper(region);
        var rule = region.FillRule == FillRule.EvenOdd ? Clipper2Lib.FillRule.EvenOdd : Clipper2Lib.FillRule.NonZero;
        var union = Clipper.Union(paths, rule);
        return FromClipper(union);
    }

    /// <summary>
    /// Offsets the region boundary by <paramref name="deltaMm"/> (negative = inset).
    /// The result can have more or fewer components than the input, or be empty.
    /// </summary>
    public static Region Offset(Region region, double deltaMm)
    {
        var normalized = ToClipper(Normalize(region));
        var result = Clipper.InflatePaths(normalized, deltaMm, JoinType.Round, EndType.Polygon, 2.0, 2);
        return FromClipper(Clipper.Union(result, Clipper2Lib.FillRule.NonZero));
    }

    /// <summary>Area shared by two regions (each resolved with its own fill rule).</summary>
    public static double IntersectionArea(Region a, Region b)
    {
        var ia = ToClipper(Normalize(a));
        var ib = ToClipper(Normalize(b));
        return Math.Abs(Clipper.Area(Clipper.Intersect(ia, ib, Clipper2Lib.FillRule.NonZero)));
    }

    /// <summary>Union of several regions (each resolved with its own fill rule first).</summary>
    public static Region Union(IEnumerable<Region> regions)
    {
        var all = new PathsD();
        foreach (var r in regions) all.AddRange(ToClipper(Normalize(r)));
        return FromClipper(Clipper.Union(all, Clipper2Lib.FillRule.NonZero));
    }

    /// <summary>
    /// Removes vertices that deviate less than <paramref name="toleranceMm"/> from the outline
    /// (Ramer–Douglas–Peucker). For coarse spatial queries, not for stitch geometry.
    /// </summary>
    public static Region Simplify(Region region, double toleranceMm)
    {
        var simplified = Clipper.RamerDouglasPeucker(ToClipper(region), toleranceMm);
        return FromClipper(simplified) with { FillRule = region.FillRule };
    }

    /// <summary>The area covered by a stroke of <paramref name="widthMm"/> along an open path (butt ends).</summary>
    public static Region BufferPath(IReadOnlyList<Vec2> path, double widthMm)
    {
        var p = new PathD(path.Count);
        foreach (var v in path) p.Add(new PointD(v.X, v.Y));
        var result = Clipper.InflatePaths(new PathsD { p }, widthMm / 2, JoinType.Round, EndType.Butt, 2.0, 2);
        return FromClipper(Clipper.Union(result, Clipper2Lib.FillRule.NonZero));
    }

    private static PathsD ToClipper(Region region)
    {
        var paths = new PathsD(region.Rings.Count);
        foreach (var ring in region.Rings)
        {
            var path = new PathD(ring.Count);
            foreach (var p in ring) path.Add(new PointD(p.X, p.Y));
            paths.Add(path);
        }

        return paths;
    }

    private static Region FromClipper(PathsD paths) =>
        new(paths.Where(p => p.Count >= 3)
            .Select(p => (IReadOnlyList<Vec2>)p.Select(pt => new Vec2(pt.x, pt.y)).ToArray())
            .ToArray(), FillRule.EvenOdd);
}
