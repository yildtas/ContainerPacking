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

    public static ObjectCoverage For(EmbroideryObject item)
    {
        var parts = new List<(Region, Bounds)>();
        void Add(Region r)
        {
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
    /// and last <paramref name="endSlackMm"/> are exempt: they sit on the edges of the objects
    /// being connected, not in open fabric.
    /// </summary>
    public static bool Hides(IReadOnlyList<ObjectCoverage> coverages, Vec2 a, Vec2 b, double stepMm = 0.5, double endSlackMm = 1.0)
    {
        if (coverages.Count == 0) return false;
        var length = Vec2.Distance(a, b);
        var n = Math.Max(2, (int)Math.Ceiling(length / stepMm));
        for (var i = 0; i <= n; i++)
        {
            var d = length * i / n;
            if (d < endSlackMm || length - d < endSlackMm) continue;
            var p = Vec2.Lerp(a, b, (double)i / n);
            if (!coverages.Any(c => c.Contains(p))) return false;
        }

        return true;
    }
}
