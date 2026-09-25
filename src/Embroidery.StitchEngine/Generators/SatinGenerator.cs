using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;

namespace Embroidery.StitchEngine.Generators;

/// <summary>
/// Satin column generator. The column is first resolved into a <see cref="SatinLadder"/>
/// (from rails+rungs or centre line+width); pull compensation widens the ladder, and throws are
/// then placed at equal steps of the compensated outer-rail advance, so the outside of a curve
/// gets the requested density and the inside gets short stitches instead of pile-ups.
/// Entry candidate 1 sews the column from its far end.
/// Codes: SAT001–SAT002, SAT004–SAT005 (see <see cref="SatinLadder"/>), SAT003 very wide column.
/// </summary>
public sealed class SatinGenerator : IStitchGenerator<SatinObject>
{
    /// <summary>Columns wider than this should normally be a fill.</summary>
    public const double TatamiRecommendedAboveMm = 12.0;

    public GenerationResult<LogicalStitchBlock> Generate(SatinObject item, GenerationContext context, CancellationToken ct = default)
    {
        var built = SatinLadder.BuildColumns(item);
        var diagnostics = new List<Diagnostic>(built.Diagnostics);
        var columns = built.Value.ToList();
        if (columns.Count == 0)
        {
            return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, []), diagnostics);
        }

        if (context.EntryCandidate == 1)
        {
            columns.Reverse();
            columns = columns.Select(c => c.Reversed()).ToList();
        }

        var p = item.Parameters;
        var stitches = new List<LogicalStitch>();
        foreach (var column in columns)
        {
            // Pieces of a corner-split column follow each other: each ends at the corner the
            // next one starts from.
            AddUnderlay(column, p.Underlay, stitches);
            AddTop(column, p, stitches);
        }

        var maxWidth = columns.Max(c => c.MaxWidth);
        if (maxWidth > TatamiRecommendedAboveMm)
        {
            diagnostics.Add(Diagnostic.Warning("SAT003",
                FormattableString.Invariant($"Satin is up to {maxWidth:0.0} mm wide; a Tatami fill is recommended above {TatamiRecommendedAboveMm} mm."),
                item.Id));
        }

        return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, stitches), diagnostics);
    }

    internal static void AddTop(SatinLadder baseLadder, SatinParameters p, List<LogicalStitch> output)
    {
        // Compensate first, then measure density on the compensated rails.
        var ladder = baseLadder.WithPullCompensation(p.PullCompensationMm);
        var spacing = Math.Max(0.1, p.SpacingMm);
        var push = Math.Clamp(p.PushCompensationMm, 0, ladder.Length * 0.45);
        var s0 = push;
        var s1 = ladder.Length - push;
        var count = Math.Max(1, (int)Math.Ceiling((s1 - s0) / spacing - 1e-9));

        var pts = new List<Vec2>(2 * (count + 1));
        Vec2? prevA = null, prevB = null;
        for (var i = 0; i <= count; i++)
        {
            var (a, b) = ladder.At(s0 + (s1 - s0) * i / count);
            var dir = (b - a).Normalized();
            var w = Vec2.Distance(a, b);

            // Short stitches: where one rail barely moves (inside of a curve), every other
            // penetration on that rail is pulled into the column to avoid a pile-up.
            if (p.ShortStitch == ShortStitchMode.InnerOnly && i % 2 == 1)
            {
                if (prevA is { } pa && Vec2.Distance(pa, a) < p.ShortStitchThresholdMm) a += dir * (w * p.ShortStitchFraction);
                if (prevB is { } pb && Vec2.Distance(pb, b) < p.ShortStitchThresholdMm) b -= dir * (w * p.ShortStitchFraction);
            }

            prevA = a;
            prevB = b;
            pts.Add(a);
            // At a pointed tip both rails meet: one penetration, not a zero-length throw.
            if (Vec2.Distance(a, b) >= 0.05) pts.Add(b);
        }

        AppendWithSplits(pts, p.MaxWidthMm, StitchLayer.Top, output);
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
    /// Underlay is built from the uncompensated ladder (support geometry, not the widened top).
    /// Layers are arranged so each one ends where the column starts, and the top stitching then
    /// runs start → end without a travel. A layer that does not fit a narrow column is skipped.
    /// </summary>
    private static void AddUnderlay(SatinLadder ladder, SatinUnderlay u, List<LogicalStitch> output)
    {
        var samples = Math.Max(2, (int)Math.Ceiling(ladder.Length / 0.5));

        Vec2 Inset(double s, bool sideA)
        {
            var (a, b) = ladder.At(s);
            var w = Vec2.Distance(a, b);
            if (w <= 2 * u.EdgeInsetMm) return Vec2.Lerp(a, b, 0.5);
            return sideA ? a + (b - a).Normalized() * u.EdgeInsetMm : b + (a - b).Normalized() * u.EdgeInsetMm;
        }

        List<Vec2> Line(Func<double, Vec2> f)
        {
            var list = new List<Vec2>(samples + 1);
            for (var i = 0; i <= samples; i++) list.Add(f(ladder.Length * i / samples));
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

        var width = ladder.MaxWidth;
        if (u.CenterWalk)
        {
            var center = Line(s =>
            {
                var (a, b) = ladder.At(s);
                return Vec2.Lerp(a, b, 0.5);
            });
            Run(center);
            center.Reverse();
            Run(center);
        }

        if (u.EdgeWalk && width > 2 * u.EdgeInsetMm + 0.3)
        {
            var edgeA = Line(s => Inset(s, true));
            var edgeB = Line(s => Inset(s, false));
            edgeB.Reverse();
            Run(edgeA);
            Run(edgeB);
        }

        if (u.ZigZag && width > 2 * u.EdgeInsetMm + 0.3)
        {
            var n = Math.Max(1, (int)Math.Ceiling(ladder.Length / Math.Max(0.5, u.ZigZagSpacingMm)));
            // Forward pass then a half-phase-shifted return pass, ending at the column start.
            for (var i = 0; i <= n; i++)
            {
                output.Add(new(Inset(ladder.Length * i / n, i % 2 == 0), StitchCommand.Stitch, StitchLayer.Underlay));
            }

            for (var i = n; i >= 0; i--)
            {
                var s = Math.Max(0, (i - 0.5) / n) * ladder.Length;
                output.Add(new(Inset(s, i % 2 != 0), StitchCommand.Stitch, StitchLayer.Underlay));
            }
        }
    }
}
