using System.Globalization;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;

namespace Embroidery.Application.Projects;

/// <summary>Geometry edits that need the server's geometry library (the editor does the simple ones).</summary>
public static class ObjectEditing
{
    private const double MinPieceMm = 0.5;

    /// <summary>
    /// Splits an object in two at the point nearest to <paramref name="at"/>: lines at that arc
    /// position, rails satins across both rails (the cut becomes a rung of each half), fills along a
    /// line through the point perpendicular to the fill direction. The first half keeps the id.
    /// </summary>
    public static (EmbroideryObject First, EmbroideryObject Second) Split(EmbroideryObject item, Vec2 at)
    {
        var secondId = Guid.NewGuid();
        var secondName = string.Create(CultureInfo.InvariantCulture, $"{item.Name} (2)");
        switch (item)
        {
            case RunObject r:
            {
                var (a, b) = SplitPath(r.Path, at);
                return (r with { Path = a }, r with { Id = secondId, Name = secondName, Path = b, EntryPoint = null });
            }

            case RopeObject r:
            {
                var (a, b) = SplitPath(r.Path, at);
                return (r with { Path = a }, r with { Id = secondId, Name = secondName, Path = b, EntryPoint = null });
            }

            case SatinObject { Source: SatinSource.Stroke } s:
            {
                var (a, b) = SplitPath(s.Centerline, at);
                return (s with { Centerline = a, EndTaperMm = 0 },
                    s with { Id = secondId, Name = secondName, Centerline = b, StartTaperMm = 0, EntryPoint = null });
            }

            case SatinObject s:
            {
                var railA = new ArcLengthPath(s.RailA);
                var sa = railA.Project(at).S;
                var pa = railA.PointAt(sa);
                var railB = new ArcLengthPath(s.RailB);
                var pb = railB.PointAt(railB.Project(pa).S);
                var (a1, a2) = SplitPath(s.RailA, pa);
                var (b1, b2) = SplitPath(s.RailB, pb);
                var cut = new Rung(pa, pb);
                // A rung belongs to the half where its end on rail A lies.
                double OnA(Rung r) => railA.Project(r.A).Distance <= railA.Project(r.B).Distance ? railA.Project(r.A).S : railA.Project(r.B).S;
                var before = s.Rungs.Where(r => OnA(r) < sa).ToList();
                var after = s.Rungs.Except(before).ToList();
                return (s with { RailA = a1, RailB = b1, Rungs = [.. before, cut] },
                    s with { Id = secondId, Name = secondName, RailA = a2, RailB = b2, Rungs = [cut, .. after], EntryPoint = null });
            }

            case TatamiObject t:
            {
                // Cut perpendicular to the rows so each half keeps full-length rows.
                var angle = t.Parameters.AngleDeg * Math.PI / 180;
                var along = new Vec2(Math.Cos(angle), Math.Sin(angle));
                var across = along.Perpendicular;
                var size = t.Region.Bounds.Width + t.Region.Bounds.Height + Vec2.Distance(at, t.Region.Bounds.Center) + 10;
                Region Half(double side)
                {
                    var o = at + along * (side * size / 2);
                    Vec2[] box = [o - along * (size / 2) - across * size, o + along * (size / 2) - across * size, o + along * (size / 2) + across * size, o - along * (size / 2) + across * size];
                    return PolygonOps.Intersection(t.Region, new Region([box], FillRule.NonZero));
                }

                var first = Half(-1);
                var second = Half(1);
                if (PolygonOps.Area(first) < 0.05 || PolygonOps.Area(second) < 0.05)
                {
                    throw new DesignValidationException("The split line does not cross the fill.");
                }

                return (t with { Region = first }, t with { Id = secondId, Name = secondName, Region = second, EntryPoint = null });
            }

            default:
                throw new DesignValidationException($"{item.StitchType} objects cannot be split.");
        }
    }

    private static (IReadOnlyList<Vec2> First, IReadOnlyList<Vec2> Second) SplitPath(IReadOnlyList<Vec2> points, Vec2 at)
    {
        var path = new ArcLengthPath(points);
        var s = path.Project(at).S;
        if (s < MinPieceMm || s > path.Length - MinPieceMm)
        {
            throw new DesignValidationException("The split point is too close to an end of the object.");
        }

        var cut = path.PointAt(s);
        var first = new List<Vec2>();
        var second = new List<Vec2> { cut };
        for (var i = 0; i < points.Count; i++)
        {
            if (path.LengthAt(i) < s - 1e-9) first.Add(points[i]);
            else if (path.LengthAt(i) > s + 1e-9) second.Add(points[i]);
        }

        first.Add(cut);
        return (first, second);
    }
}
