using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;

namespace Embroidery.StitchEngine.Sequencing;

public sealed record SequenceCost(double TravelMm, int ColorChanges)
{
    /// <summary>A colour change costs as much as this much travel (stop, thread change, trims).</summary>
    public const double ColorChangePenaltyMm = 250;

    public double Total => TravelMm + ColorChanges * ColorChangePenaltyMm;
}

public sealed record SequenceResult(IReadOnlyList<Guid> Order, SequenceCost Before, SequenceCost After, int Constraints)
{
    public bool Improved => After.Total < Before.Total - 1e-6;
}

/// <summary>
/// Reorders objects to reduce colour changes and frame travel while keeping every overlapping
/// pair in its original order (whatever is drawn on top is sewn later). Greedy: among objects
/// whose overlapping predecessors are done, prefer the current thread, then the nearest entry.
/// Hidden objects keep their relative order at the end.
/// </summary>
public static class SequenceOptimizer
{
    public static SequenceResult Optimize(Design design, CancellationToken ct = default)
    {
        var items = design.Objects.Where(o => o.Visible).ToList();
        var shapes = items.Select(Shape).ToList();
        var n = items.Count;

        // Precedence: i before j when they overlap and i comes first in the design.
        var preds = new List<int>[n];
        var constraints = 0;
        for (var j = 0; j < n; j++)
        {
            preds[j] = [];
            for (var i = 0; i < j; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (Overlap(shapes[i], shapes[j]))
                {
                    preds[j].Add(i);
                    constraints++;
                }
            }
        }

        var done = new bool[n];
        var order = new List<int>(n);
        Vec2? position = null;
        int? thread = null;
        while (order.Count < n)
        {
            var ready = Enumerable.Range(0, n).Where(k => !done[k] && preds[k].All(p => done[p])).ToList();
            var sameThread = ready.Where(k => items[k].ThreadIndex == thread).ToList();
            var pool = sameThread.Count > 0 ? sameThread : ready;
            // Start where the designer started: the original first ready object.
            var next = position is null
                ? pool.Min()
                : pool.MinBy(k => EntryDistance(items[k], position.Value));
            done[next] = true;
            order.Add(next);
            position = ExitAfter(items[next], position);
            thread = items[next].ThreadIndex;
        }

        var hidden = design.Objects.Where(o => !o.Visible).Select(o => o.Id);
        var optimized = order.Select(k => items[k].Id).Concat(hidden).ToList();
        return new SequenceResult(optimized, Cost(items), Cost(order.Select(k => items[k]).ToList()), constraints);
    }

    /// <summary>Approximate cost of a sequence: frame travel between objects plus colour changes.</summary>
    public static SequenceCost Cost(IReadOnlyList<EmbroideryObject> sequence)
    {
        double travel = 0;
        var colors = 0;
        Vec2? position = null;
        int? thread = null;
        foreach (var item in sequence)
        {
            if (position is { } p) travel += EntryDistance(item, p);
            if (thread is { } t && t != item.ThreadIndex) colors++;
            position = ExitAfter(item, position);
            thread = item.ThreadIndex;
        }

        return new SequenceCost(travel, colors);
    }

    private static double EntryDistance(EmbroideryObject item, Vec2 from) =>
        EntryCandidates.For(item).Min(c => Vec2.Distance(c, from));

    /// <summary>
    /// Where sewing ends: the far end for line-like objects entered at the nearest end
    /// (a rope ends where it starts); the entry corner for fills.
    /// </summary>
    private static Vec2 ExitAfter(EmbroideryObject item, Vec2? from)
    {
        var candidates = EntryCandidates.For(item);
        var entry = from is { } f ? candidates.MinBy(c => Vec2.Distance(c, f)) : candidates[0];
        if (item is TatamiObject or RopeObject || candidates.Count < 2) return entry;
        return candidates.MaxBy(c => Vec2.Distance(c, entry));
    }

    private sealed record ShapeInfo(Bounds Bounds, Region? Region);

    private static ShapeInfo Shape(EmbroideryObject item)
    {
        Region? region = item switch
        {
            RunObject r when r.Path.Count >= 2 => PolygonOps.BufferPath(r.Path, 0.6),
            _ => null,
        };
        if (region is null)
        {
            var coverage = ObjectCoverage.For(item);
            var rings = coverage.RegionsOverlapping(coverage.Bounds).SelectMany(x => x.Rings).ToArray();
            region = rings.Length > 0 ? new Region(rings, FillRule.NonZero) : null;
        }

        return new ShapeInfo(item.Bounds, region);
    }

    private static bool Overlap(ShapeInfo a, ShapeInfo b)
    {
        var x = a.Bounds;
        var y = b.Bounds;
        if (x.IsEmpty || y.IsEmpty || x.MinX > y.MaxX || y.MinX > x.MaxX || x.MinY > y.MaxY || y.MinY > x.MaxY) return false;
        if (a.Region is null || b.Region is null) return true; // unknown shape: be conservative
        return PolygonOps.IntersectionArea(a.Region, b.Region) > 0.05;
    }
}
