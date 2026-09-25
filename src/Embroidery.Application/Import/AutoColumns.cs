using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.Application.Import;

/// <summary>
/// Satin columns proposed for a filled outline. <see cref="Coverage"/> is the intersection over
/// union of the columns' area with the outline (1 = exact); <see cref="Reason"/> says why the
/// proposal was rejected, or is null when it was accepted.
/// </summary>
public sealed record AutoColumnProposal(IReadOnlyList<SatinObject> Columns, double Coverage, double MaxWidthMm, string? Reason)
{
    public bool Accepted => Reason is null;
}

/// <summary>
/// Turns a narrow filled outline (letters, leaves, scrolls) into rails-and-rungs satin columns,
/// like the automatic column tools of commercial digitizers:
/// medial skeleton → one column per branch (tips extended to the outline) → rails from rays
/// cast along the normal to the exact outline → rungs every <see cref="RungEveryMm"/>.
/// A proposal is accepted when the columns cover the outline well (IoU ≥ <see cref="MinCoverage"/>)
/// and no column is wider than the limit; otherwise the caller keeps a tatami fill.
/// </summary>
public static class AutoColumns
{
    public const double DefaultMaxWidthMm = 7.0;
    public const double MinCoverage = 0.85;
    public const double SampleMm = 0.5;
    public const double RungEveryMm = 2.5;
    public const double CornerTurnDeg = 60;

    public static AutoColumnProposal Propose(Region region, SatinObject template, double maxWidthMm = DefaultMaxWidthMm)
    {
        region = PolygonOps.Normalize(region);
        var area = PolygonOps.Area(region);
        var perimeter = region.Rings.Sum(PolygonOps.Perimeter);
        if (area < 0.5 || perimeter <= 0) return Reject("the shape is too small");

        // For a band of width w and length L, 2A/P ≈ w: a cheap first width estimate.
        var estimate = 2 * area / perimeter;
        if (estimate > maxWidthMm) return Reject(FormattableString.Invariant($"the shape is about {estimate:0.0} mm wide"));

        var resolution = Math.Clamp(estimate / 8, 0.05, 0.3);
        var branches = RegionSkeleton.Compute(region, resolution)
            .Where(b => b.Length >= Math.Max(0.8, 3 * resolution))
            .OrderByDescending(b => b.Length)
            .ToList();
        if (branches.Count == 0) return Reject("no centre line was found");

        var segments = region.Rings.SelectMany(ring => ring.Select((p, i) => (A: p, B: ring[(i + 1) % ring.Count]))).ToArray();
        var columns = new List<SatinObject>();
        foreach (var piece in branches.SelectMany(b => SplitAtCorners(b, resolution)))
        {
            if (Column(piece, region, segments, resolution) is not { } rails) continue;
            columns.Add(template with
            {
                Id = columns.Count == 0 ? template.Id : Guid.NewGuid(),
                Name = template.Name,
                Source = SatinSource.Rails,
                RailA = rails.A,
                RailB = rails.B,
                Rungs = rails.Rungs,
                Centerline = [],
            });
        }

        if (columns.Count == 0) return Reject("no column could be fitted");
        if (columns.Count > 1)
        {
            columns = columns.Select((c, i) => c with { Name = $"{template.Name} {i + 1}" }).ToList();
        }

        // Confidence: how well the columns reproduce the outline.
        var outlines = new List<Region>();
        var widest = 0.0;
        foreach (var column in columns)
        {
            foreach (var ladder in SatinLadder.BuildColumns(column).Value)
            {
                widest = Math.Max(widest, ladder.MaxWidth);
                outlines.Add(new Region([ladder.A.Concat(ladder.B.Reverse()).ToArray()], FillRule.NonZero));
            }
        }

        var union = PolygonOps.Union(outlines);
        var shared = PolygonOps.IntersectionArea(union, region);
        var coverage = shared / Math.Max(1e-9, PolygonOps.Area(union) + area - shared);
        string? reason = null;
        if (widest > maxWidthMm) reason = FormattableString.Invariant($"a column would be {widest:0.0} mm wide (limit {maxWidthMm:0.0} mm)");
        else if (coverage < MinCoverage) reason = FormattableString.Invariant($"columns cover only {coverage:P0} of the shape");
        return new AutoColumnProposal(columns, coverage, widest, reason);

        AutoColumnProposal Reject(string why) => new([], 0, 0, why);
    }

    /// <summary>A stretch of skeleton sewn as one column; ends marked Extend reach out to the outline.</summary>
    private sealed record Piece(List<Vec2> Points, List<double> Radius, bool ExtendStart, bool ExtendEnd, bool Closed);

    /// <summary>
    /// Splits a branch where it turns sharply (over <see cref="CornerTurnDeg"/>), like a stroke
    /// column's corner split; both pieces run on through the corner so they lap over each other.
    /// </summary>
    private static IEnumerable<Piece> SplitAtCorners(SkeletonBranch branch, double resolution)
    {
        var points = branch.Points.ToList();
        var radius = branch.Radius.ToList();
        var closed = !branch.StartsAtTip && !branch.EndsAtTip && points.Count > 8
            && Vec2.Distance(points[0], points[^1]) < 4 * resolution;
        if (closed)
        {
            yield return new Piece([.. points, points[0]], [.. radius, radius[0]], false, false, true);
            yield break;
        }

        var cumulative = new double[points.Count];
        for (var k = 1; k < points.Count; k++) cumulative[k] = cumulative[k - 1] + Vec2.Distance(points[k - 1], points[k]);
        int At(double s) => Math.Clamp(Array.BinarySearch(cumulative, s) is var i && i < 0 ? ~i : i, 0, points.Count - 1);

        var cuts = new List<int>();
        var lastCut = double.NegativeInfinity;
        for (var k = 1; k < points.Count - 1; k++)
        {
            var window = Math.Max(0.8, radius[k]);
            if (cumulative[k] < window || cumulative[^1] - cumulative[k] < window || cumulative[k] - lastCut < 2 * window) continue;
            var turn = TurnDeg(points[At(cumulative[k] - window)], points[k], points[At(cumulative[k] + window)]);
            if (turn < CornerTurnDeg) continue;
            // Take the sharpest point of this corner.
            var best = k;
            for (var m = k + 1; m < points.Count - 1 && cumulative[m] - cumulative[k] < window; m++)
            {
                if (TurnDeg(points[At(cumulative[m] - window)], points[m], points[At(cumulative[m] + window)]) > TurnDeg(points[At(cumulative[best] - window)], points[best], points[At(cumulative[best] + window)])) best = m;
            }

            cuts.Add(best);
            lastCut = cumulative[best];
            k = best;
        }

        var start = 0;
        foreach (var cut in cuts.Append(points.Count - 1))
        {
            var first = start == 0;
            var last = cut == points.Count - 1;
            yield return new Piece(
                points.GetRange(start, cut - start + 1), radius.GetRange(start, cut - start + 1),
                first ? branch.StartsAtTip : true, last ? branch.EndsAtTip : true, false);
            start = cut;
        }
    }

    private static double TurnDeg(Vec2 a, Vec2 b, Vec2 c)
    {
        var d1 = b - a;
        var d2 = c - b;
        if (d1.Length < 1e-9 || d2.Length < 1e-9) return 0;
        var cos = Math.Clamp(Vec2.Dot(d1.Normalized(), d2.Normalized()), -1, 1);
        return Math.Acos(cos) * 180 / Math.PI;
    }

    private static (Vec2[] A, Vec2[] B, Rung[] Rungs)? Column(Piece piece, Region region, (Vec2 A, Vec2 B)[] segments, double resolution)
    {
        var points = piece.Points.ToList();
        var radius = piece.Radius;
        var closed = piece.Closed;
        if (points.Count < 2) return null;

        // Tips of the skeleton stop about one radius short of the outline's point; corner ends
        // run on by one radius so the two columns lap over each other.
        if (piece.ExtendStart) points.Insert(0, points[0] + Direction(points, fromStart: true) * (radius[0] + resolution));
        if (piece.ExtendEnd) points.Add(points[^1] + Direction(points, fromStart: false) * (radius[^1] + resolution));

        var path = new ArcLengthPath(points);
        var n = Math.Max(2, (int)Math.Ceiling(path.Length / SampleMm));
        var centre = Enumerable.Range(0, n + 1).Select(i => path.PointAt(path.Length * i / n)).ToArray();
        var a = new List<Vec2>();
        var b = new List<Vec2>();
        var rungs = new List<Rung>();
        var rungStep = Math.Max(1, (int)Math.Round(RungEveryMm / (path.Length / n)));
        for (var i = 0; i <= n; i++)
        {
            var c = centre[i];
            if (!PolygonOps.Contains(region, c)) continue;
            var prev = centre[closed && i == 0 ? n - 2 : Math.Max(0, i - 2)];
            var next = centre[closed && i == n ? 2 : Math.Min(n, i + 2)];
            var normal = (next - prev).Normalized().Perpendicular;

            // Local radius from the distance field; rays that run into another arm of the shape
            // (at junctions) fall back to it.
            var r = radius[Math.Clamp((int)Math.Round((double)i / n * (radius.Count - 1)), 0, radius.Count - 1)];
            var limit = Math.Max(1.6 * r, r + 1.0);
            var hitA = Ray(c, normal, segments) is { } ha && ha <= limit;
            var hitB = Ray(c, -normal, segments) is { } hb && hb <= limit;
            var da = hitA ? Ray(c, normal, segments)!.Value : r;
            var db = hitB ? Ray(c, -normal, segments)!.Value : r;
            a.Add(c + normal * Math.Max(0.1, da));
            b.Add(c - normal * Math.Max(0.1, db));

            // Rungs only where both rails are measured (not at junctions or laps) and never
            // crossing the previous one.
            var rung = new Rung(a[^1], b[^1]);
            if (i % rungStep == 0 && hitA && hitB && a.Count > 1
                && (rungs.Count == 0 || (Vec2.Distance(Mid(rungs[^1]), Mid(rung)) >= 1.0 && !Cross(rungs[^1], rung))))
            {
                rungs.Add(rung);
            }
        }

        return a.Count >= 2 ? (a.ToArray(), b.ToArray(), rungs.ToArray()) : null;
    }

    private static Vec2 Mid(Rung r) => Vec2.Lerp(r.A, r.B, 0.5);

    private static bool Cross(Rung p, Rung q)
    {
        double Side(Vec2 a, Vec2 b, Vec2 c) => Vec2.Cross(b - a, c - a);
        return Side(p.A, p.B, q.A) * Side(p.A, p.B, q.B) < 0 && Side(q.A, q.B, p.A) * Side(q.A, q.B, p.B) < 0;
    }

    private static Vec2 Direction(List<Vec2> points, bool fromStart)
    {
        // Average direction over the first/last ~1 mm, pointing outwards.
        var tip = fromStart ? points[0] : points[^1];
        var inner = tip;
        double walked = 0;
        for (var k = 1; k < points.Count && walked < 1.0; k++)
        {
            var next = fromStart ? points[k] : points[^(k + 1)];
            walked += Vec2.Distance(inner, next);
            inner = next;
        }

        var d = tip - inner;
        return d.Length < 1e-9 ? Vec2.Zero : d.Normalized();
    }

    /// <summary>Distance along the ray to the nearest outline crossing, if any.</summary>
    private static double? Ray(Vec2 origin, Vec2 direction, (Vec2 A, Vec2 B)[] segments)
    {
        double? best = null;
        foreach (var (p, q) in segments)
        {
            var e = q - p;
            var denom = Vec2.Cross(direction, e);
            if (Math.Abs(denom) < 1e-12) continue;
            var w = p - origin;
            var t = Vec2.Cross(w, e) / denom;
            var u = Vec2.Cross(w, direction) / denom;
            if (t > 1e-9 && u >= 0 && u <= 1 && (best is null || t < best)) best = t;
        }

        return best;
    }
}
