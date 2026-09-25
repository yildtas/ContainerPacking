using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.StitchEngine.Sequencing;

/// <summary>
/// The area an object's top stitching will cover, used to decide whether a connector sewn
/// before it ends up hidden underneath. Run objects cover nothing meaningful.
/// </summary>
public sealed class ObjectCoverage
{
    private readonly List<(Region Region, Bounds Bounds)> _parts;

    private ObjectCoverage(List<(Region, Bounds)> parts) => _parts = parts;

    public static readonly ObjectCoverage None = new([]);

    public bool IsEmpty => _parts.Count == 0;

    public Bounds Bounds => _parts.Aggregate(Bounds.Empty, (b, p) => b.Include(p.Bounds));

    public bool Overlaps(Bounds box)
    {
        foreach (var (_, b) in _parts)
        {
            if (b.MinX <= box.MaxX && b.MaxX >= box.MinX && b.MinY <= box.MaxY && b.MaxY >= box.MinY) return true;
        }

        return false;
    }

    public static ObjectCoverage For(EmbroideryObject item)
    {
        var parts = new List<(Region, Bounds)>();
        void Add(Region r)
        {
            // Coverage only answers "is this point under stitching"; 0.15 mm of outline detail
            // is irrelevant there and simplifying makes point queries an order of magnitude faster.
            r = PolygonOps.Simplify(r, 0.15);
            if (r.Rings.Count > 0) parts.Add((r, r.Bounds));
        }

        switch (item)
        {
            case SatinObject s:
                foreach (var ladder in SatinLadder.BuildColumns(s).Value)
                {
                    Add(new Region([ladder.A.Concat(ladder.B.Reverse()).ToArray()], FillRule.NonZero));
                }

                break;
            case TatamiObject t:
                Add(PolygonOps.Normalize(t.Region));
                break;
            case RopeObject r when r.Path.Count >= 2:
                Add(PolygonOps.BufferPath(r.Path, r.WidthMm));
                break;
        }

        return new ObjectCoverage(parts);
    }

    /// <summary>The coverage regions whose bounds reach <paramref name="box"/>.</summary>
    public IEnumerable<Region> RegionsOverlapping(Bounds box)
    {
        foreach (var (region, b) in _parts)
        {
            if (b.MinX <= box.MaxX && b.MaxX >= box.MinX && b.MinY <= box.MaxY && b.MaxY >= box.MinY) yield return region;
        }
    }

    public bool Contains(Vec2 p)
    {
        foreach (var (region, b) in _parts)
        {
            if (p.X < b.MinX || p.X > b.MaxX || p.Y < b.MinY || p.Y > b.MaxY) continue;
            if (PolygonOps.Contains(region, p)) return true;
        }

        return false;
    }

    /// <summary>
    /// True when every sample of the segment lies under at least one of the coverages. The first
    /// <paramref name="endSlackMm"/> (and last <paramref name="endSlackEndMm"/>, same by default)
    /// are exempt: they sit on the edges of the objects being connected, not in open fabric.
    /// </summary>
    public static bool Hides(IReadOnlyList<ObjectCoverage> coverages, Vec2 a, Vec2 b, double stepMm = 0.5,
        double endSlackMm = 1.0, double? endSlackEndMm = null)
    {
        if (coverages.Count == 0) return false;
        var slackEnd = endSlackEndMm ?? endSlackMm;
        var length = Vec2.Distance(a, b);
        var n = Math.Max(2, (int)Math.Ceiling(length / stepMm));
        for (var i = 0; i <= n; i++)
        {
            var d = length * i / n;
            if (d < endSlackMm || length - d < slackEnd) continue;
            var p = Vec2.Lerp(a, b, (double)i / n);
            if (!coverages.Any(c => c.Contains(p))) return false;
        }

        return true;
    }
}
