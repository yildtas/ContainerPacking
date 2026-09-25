using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.Geometry.Svg;

namespace Embroidery.Application.Import;

/// <summary>
/// Turns imported artwork into editable embroidery objects with sensible defaults:
/// filled shapes become Tatami, wide strokes Satin, thin strokes Run.
/// Codes: IMP001 shape skipped.
/// </summary>
public static class ObjectFactory
{
    /// <summary>Strokes at least this wide become satin columns.</summary>
    public const double SatinStrokeMinMm = 1.2;

    public static (IReadOnlyList<EmbroideryThread> Threads, IReadOnlyList<EmbroideryObject> Objects, IReadOnlyList<Diagnostic> Diagnostics)
        FromArtwork(ImportedArtwork artwork)
    {
        var threads = new List<EmbroideryThread>();
        var objects = new List<EmbroideryObject>();
        var diagnostics = new List<Diagnostic>();

        int ThreadFor(string color)
        {
            var idx = threads.FindIndex(t => t.ColorHex.Equals(color, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) return idx;
            threads.Add(new EmbroideryThread($"İplik {threads.Count + 1}", color));
            return threads.Count - 1;
        }

        var n = 0;
        foreach (var shape in artwork.Shapes)
        {
            var baseName = shape.ElementId ?? $"Şekil {++n}";
            if (shape.FillColor is { } fill)
            {
                var rings = shape.Subpaths.Where(s => s.Points.Count >= 3).Select(s => s.Points).ToArray();
                var region = new Region(rings, shape.FillRule);
                if (rings.Length > 0 && PolygonOps.Area(region) >= 0.05)
                {
                    objects.Add(new TatamiObject { Id = Guid.NewGuid(), Name = baseName, ThreadIndex = ThreadFor(fill), Region = region });
                }
                else
                {
                    diagnostics.Add(Diagnostic.Info("IMP001", $"'{baseName}' has no fillable area and was skipped."));
                }
            }

            if (shape.StrokeColor is { } stroke)
            {
                var thread = ThreadFor(stroke);
                var i = 0;
                foreach (var sub in shape.Subpaths)
                {
                    var path = sub.Closed ? sub.Points.Append(sub.Points[0]).ToArray() : sub.Points.ToArray();
                    if (path.Length < 2) continue;
                    var name = shape.Subpaths.Count > 1 ? $"{baseName} kontur {++i}" : $"{baseName} kontur";
                    if (shape.StrokeWidthMm >= SatinStrokeMinMm)
                    {
                        var (a, b) = ObjectConverter.RailsAround(path, shape.StrokeWidthMm);
                        objects.Add(new SatinObject { Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, RailA = a, RailB = b });
                    }
                    else
                    {
                        objects.Add(new RunObject { Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, Path = path });
                    }
                }
            }
        }

        if (threads.Count == 0) threads.Add(new EmbroideryThread("İplik 1", "#000000"));
        return (threads, objects, diagnostics);
    }
}

/// <summary>Converts an object to another stitch type, keeping its geometry as closely as possible.</summary>
public static class ObjectConverter
{
    public const double DefaultSatinWidthMm = 3.0;

    public static EmbroideryObject Convert(EmbroideryObject item, StitchType target)
    {
        if (item.StitchType == target) return item;
        return (item, target) switch
        {
            (RunObject r, StitchType.Satin) => Satin(item, RailsAround(r.Path, DefaultSatinWidthMm)),
            (RunObject r, StitchType.Tatami) => Tatami(item, RegionFromPath(r.Path)),
            (SatinObject s, StitchType.Run) => Run(item, Centerline(s)),
            (SatinObject s, StitchType.Tatami) => Tatami(item, [s.RailA.Concat(s.RailB.Reverse()).ToArray()]),
            (TatamiObject t, StitchType.Run) => Run(item, OuterRingPath(t.Region)),
            (TatamiObject t, StitchType.Satin) => Satin(item, SplitRing(LargestRing(t.Region))),
            _ => throw new InvalidOperationException($"Cannot convert {item.StitchType} to {target}."),
        };
    }

    private static RunObject Run(EmbroideryObject from, IReadOnlyList<Vec2> path) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, Path = path };

    private static SatinObject Satin(EmbroideryObject from, (IReadOnlyList<Vec2> A, IReadOnlyList<Vec2> B) rails) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, RailA = rails.A, RailB = rails.B };

    private static TatamiObject Tatami(EmbroideryObject from, IReadOnlyList<Vec2>[] rings) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, Region = new Region(rings) };

    /// <summary>A closed outline fills its inside; an open path becomes a band of the default satin width.</summary>
    private static IReadOnlyList<Vec2>[] RegionFromPath(IReadOnlyList<Vec2> path)
    {
        var closed = path.Count > 3 && path[0].ApproximatelyEquals(path[^1], 1e-6);
        if (closed && Math.Abs(PolygonOps.SignedArea(path)) > 0.05) return [path.Take(path.Count - 1).ToArray()];
        return PolygonOps.BufferPath(path, DefaultSatinWidthMm).Rings.ToArray();
    }

    /// <summary>Two rails offset ±width/2 from a centre path (miter joins, clamped at sharp corners).</summary>
    public static (IReadOnlyList<Vec2> A, IReadOnlyList<Vec2> B) RailsAround(IReadOnlyList<Vec2> path, double width)
    {
        var h = width / 2;
        var a = new Vec2[path.Count];
        var b = new Vec2[path.Count];
        var closed = path.Count > 2 && path[0].ApproximatelyEquals(path[^1], 1e-6);
        for (var i = 0; i < path.Count; i++)
        {
            Vec2 prev, next;
            if (closed)
            {
                prev = i == 0 ? path[^2] : path[i - 1];
                next = i == path.Count - 1 ? path[1] : path[i + 1];
            }
            else
            {
                prev = i == 0 ? path[i] : path[i - 1];
                next = i == path.Count - 1 ? path[i] : path[i + 1];
            }

            var d1 = (path[i] - prev).Normalized();
            var d2 = (next - path[i]).Normalized();
            if (d1 == Vec2.Zero) d1 = d2;
            if (d2 == Vec2.Zero) d2 = d1;
            var n1 = d1.Perpendicular;
            var miter = (n1 + d2.Perpendicular).Normalized();
            if (miter == Vec2.Zero) miter = n1;
            var scale = Math.Min(h / Math.Max(0.25, Vec2.Dot(miter, n1)), 2 * h);
            a[i] = path[i] - miter * scale;
            b[i] = path[i] + miter * scale;
        }

        return (a, b);
    }

    private static IReadOnlyList<Vec2> Centerline(SatinObject s)
    {
        var a = new ArcLengthPath(s.RailA);
        var b = new ArcLengthPath(s.RailB);
        var n = Math.Max(2, (int)Math.Ceiling(Math.Max(a.Length, b.Length) / 0.5));
        return Enumerable.Range(0, n + 1).Select(i => Vec2.Lerp(a.PointAtFraction((double)i / n), b.PointAtFraction((double)i / n), 0.5)).ToArray();
    }

    private static IReadOnlyList<Vec2> LargestRing(Region region) =>
        region.Rings.MaxBy(r => Math.Abs(PolygonOps.SignedArea(r))) ?? [];

    private static IReadOnlyList<Vec2> OuterRingPath(Region region)
    {
        var ring = LargestRing(region);
        return ring.Count == 0 ? [] : ring.Append(ring[0]).ToArray();
    }

    /// <summary>
    /// Splits a closed outline into two rails at its two most distant vertices. Good for
    /// elongated shapes; users refine the rails afterwards.
    /// </summary>
    public static (IReadOnlyList<Vec2> A, IReadOnlyList<Vec2> B) SplitRing(IReadOnlyList<Vec2> ring)
    {
        if (ring.Count < 3) return (ring, ring);
        var step = Math.Max(1, ring.Count / 400);
        int bi = 0, bj = 0;
        double best = -1;
        for (var i = 0; i < ring.Count; i += step)
        {
            for (var j = i + 1; j < ring.Count; j += step)
            {
                var d = (ring[i] - ring[j]).LengthSquared;
                if (d > best) (best, bi, bj) = (d, i, j);
            }
        }

        var a = new List<Vec2>();
        for (var k = bi; ; k = (k + 1) % ring.Count)
        {
            a.Add(ring[k]);
            if (k == bj) break;
        }

        var b = new List<Vec2>();
        for (var k = bi; ; k = (k - 1 + ring.Count) % ring.Count)
        {
            b.Add(ring[k]);
            if (k == bj) break;
        }

        return (a, b);
    }
}
