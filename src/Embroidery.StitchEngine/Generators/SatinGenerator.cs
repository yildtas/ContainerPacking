using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Geometry;

namespace Embroidery.StitchEngine.Generators;

/// <summary>
/// Satin column between two rails. Rails are matched by normalised arc length; the
/// penetration count follows the longer rail so the outside of a curve gets no gaps.
/// Entry candidate 1 sews the column from its far end.
/// Codes: SAT001 invalid rails, SAT002 rail direction fixed, SAT003 very wide column.
/// </summary>
public sealed class SatinGenerator : IStitchGenerator<SatinObject>
{
    /// <summary>Columns wider than this should normally be a fill.</summary>
    public const double TatamiRecommendedAboveMm = 12.0;

    public GenerationResult<LogicalStitchBlock> Generate(SatinObject item, GenerationContext context, CancellationToken ct = default)
    {
        var diagnostics = new List<Diagnostic>();
        var empty = new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, []);
        if (item.RailA.Count < 2 || item.RailB.Count < 2)
        {
            diagnostics.Add(Diagnostic.Error("SAT001", "Satin needs two rails with at least two points each.", item.Id));
            return new(empty, diagnostics);
        }

        var railA = new ArcLengthPath(item.RailA);
        var railB = new ArcLengthPath(item.RailB);
        if (railA.Length < 1e-6 || railB.Length < 1e-6)
        {
            diagnostics.Add(Diagnostic.Error("SAT001", "Satin rails must have non-zero length.", item.Id));
            return new(empty, diagnostics);
        }

        var straight = Vec2.Distance(railA.Start, railB.Start) + Vec2.Distance(railA.End, railB.End);
        var crossed = Vec2.Distance(railA.Start, railB.End) + Vec2.Distance(railA.End, railB.Start);
        if (crossed < straight)
        {
            railB = railB.Reversed();
            diagnostics.Add(Diagnostic.Info("SAT002", "Rails ran in opposite directions; rail B was reversed.", item.Id));
        }

        if (context.EntryCandidate == 1)
        {
            railA = railA.Reversed();
            railB = railB.Reversed();
        }

        var p = item.Parameters;
        var stitches = new List<LogicalStitch>();
        AddUnderlay(railA, railB, p.Underlay, stitches);
        var maxWidth = AddTop(railA, railB, p, stitches);

        if (maxWidth > TatamiRecommendedAboveMm)
        {
            diagnostics.Add(Diagnostic.Warning("SAT003",
                FormattableString.Invariant($"Satin is up to {maxWidth:0.0} mm wide; a Tatami fill is recommended above {TatamiRecommendedAboveMm} mm."),
                item.Id));
        }

        return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, stitches), diagnostics);
    }

    private static double AddTop(ArcLengthPath railA, ArcLengthPath railB, SatinParameters p, List<LogicalStitch> output)
    {
        var spacing = Math.Max(0.1, p.SpacingMm);
        var count = Math.Max(1, (int)Math.Ceiling(Math.Max(railA.Length, railB.Length) / spacing));

        // Push compensation: pull the column ends inward along the column.
        var avg = (railA.Length + railB.Length) / 2;
        var tPush = Math.Clamp(p.PushCompensationMm / avg, 0, 0.45);
        var t0 = tPush;
        var t1 = 1 - tPush;

        var pts = new List<Vec2>(2 * (count + 1));
        Vec2? prevA = null, prevB = null;
        double maxWidth = 0;
        for (var i = 0; i <= count; i++)
        {
            var t = t0 + (t1 - t0) * i / count;
            var a = railA.PointAtFraction(t);
            var b = railB.PointAtFraction(t);
            var dir = (b - a).Normalized();
            var width = Vec2.Distance(a, b);
            maxWidth = Math.Max(maxWidth, width);

            // Pull compensation: widen the throw, half on each side.
            a -= dir * (p.PullCompensationMm / 2);
            b += dir * (p.PullCompensationMm / 2);
            var w = Vec2.Distance(a, b);

            // Short stitches: on the inside of tight curves every other penetration moves inward.
            if (p.ShortStitch == ShortStitchMode.InnerOnly && i % 2 == 1)
            {
                if (prevA is { } pa && Vec2.Distance(pa, a) < p.ShortStitchThresholdMm) a += dir * (w * p.ShortStitchFraction);
                if (prevB is { } pb && Vec2.Distance(pb, b) < p.ShortStitchThresholdMm) b -= dir * (w * p.ShortStitchFraction);
            }

            prevA = a;
            prevB = b;
            pts.Add(a);
            pts.Add(b);
        }

        AppendWithSplits(pts, p.MaxWidthMm, StitchLayer.Top, output);
        return maxWidth;
    }

    /// <summary>
    /// Adds penetrations, splitting any stitch longer than <paramref name="maxLength"/>.
    /// The split phase alternates so split points do not line up into a visible groove.
    /// </summary>
    private static void AppendWithSplits(List<Vec2> pts, double maxLength, StitchLayer layer, List<LogicalStitch> output)
    {
        maxLength = Math.Max(0.5, maxLength);
        for (var i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                var from = pts[i - 1];
                var to = pts[i];
                var len = Vec2.Distance(from, to);
                if (len > maxLength)
                {
                    var k = (int)Math.Ceiling(len / maxLength);
                    var phase = (i / 2) % 2 == 0 ? 0.0 : 0.5;
                    for (var j = 1; j < k + (phase > 0 ? 1 : 0); j++)
                    {
                        var f = (j - phase) / k;
                        if (f is > 0 and < 1) output.Add(new(Vec2.Lerp(from, to, f), StitchCommand.Stitch, layer));
                    }
                }
            }

            output.Add(new(pts[i], StitchCommand.Stitch, layer));
        }
    }

    /// <summary>
    /// Underlay layers are arranged so that each one ends where the column starts, and the
    /// top stitching then runs start → end without a travel.
    /// </summary>
    private static void AddUnderlay(ArcLengthPath railA, ArcLengthPath railB, SatinUnderlay u, List<LogicalStitch> output)
    {
        var samples = Math.Max(2, (int)Math.Ceiling(Math.Max(railA.Length, railB.Length) / 0.5));
        Vec2 Inset(ArcLengthPath from, ArcLengthPath to, double t, double inset)
        {
            var a = from.PointAtFraction(t);
            var b = to.PointAtFraction(t);
            var w = Vec2.Distance(a, b);
            return w <= 2 * inset ? Vec2.Lerp(a, b, 0.5) : a + (b - a).Normalized() * inset;
        }

        List<Vec2> Line(Func<double, Vec2> f)
        {
            var list = new List<Vec2>(samples + 1);
            for (var i = 0; i <= samples; i++) list.Add(f((double)i / samples));
            return list;
        }

        void Run(List<Vec2> path)
        {
            foreach (var pt in RunSampler.Sample(path, u.StitchLengthMm, 20))
            {
                // Consecutive layers share their turning point; do not sew it twice.
                if (output.Count > 0 && output[^1].Position.ApproximatelyEquals(pt, 1e-6)) continue;
                output.Add(new(pt, StitchCommand.Stitch, StitchLayer.Underlay));
            }
        }

        if (u.CenterWalk)
        {
            var center = Line(t => Vec2.Lerp(railA.PointAtFraction(t), railB.PointAtFraction(t), 0.5));
            Run(center);
            center.Reverse();
            Run(center);
        }

        if (u.EdgeWalk)
        {
            var edgeA = Line(t => Inset(railA, railB, t, u.EdgeInsetMm));
            var edgeB = Line(t => Inset(railB, railA, t, u.EdgeInsetMm));
            edgeB.Reverse();
            Run(edgeA);
            Run(edgeB);
        }

        if (u.ZigZag)
        {
            var len = Math.Max(railA.Length, railB.Length);
            var n = Math.Max(1, (int)Math.Ceiling(len / Math.Max(0.5, u.ZigZagSpacingMm)));
            // Forward pass then a half-phase-shifted return pass, ending at the column start.
            for (var i = 0; i <= n; i++)
            {
                var t = (double)i / n;
                var pt = i % 2 == 0 ? Inset(railA, railB, t, u.EdgeInsetMm) : Inset(railB, railA, t, u.EdgeInsetMm);
                output.Add(new(pt, StitchCommand.Stitch, StitchLayer.Underlay));
            }

            for (var i = n; i >= 0; i--)
            {
                var t = Math.Max(0, (i - 0.5) / n);
                var pt = i % 2 == 0 ? Inset(railB, railA, t, u.EdgeInsetMm) : Inset(railA, railB, t, u.EdgeInsetMm);
                output.Add(new(pt, StitchCommand.Stitch, StitchLayer.Underlay));
            }
        }
    }
}
