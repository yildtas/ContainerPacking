using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Geometry;

namespace Embroidery.StitchEngine.Generators;

/// <summary>
/// Tatami fill with optional edge-run and perpendicular fill underlay.
/// Codes: TAT001 empty region, TAT002 jumps used between fill sections.
/// </summary>
public sealed class TatamiGenerator : IStitchGenerator<TatamiObject>
{
    public GenerationResult<LogicalStitchBlock> Generate(TatamiObject item, GenerationContext context, CancellationToken ct = default)
    {
        var diagnostics = new List<Diagnostic>();
        var region = PolygonOps.Normalize(item.Region);
        var stitches = new List<LogicalStitch>();
        if (region.Rings.Count == 0 || PolygonOps.Area(region) < 0.01)
        {
            diagnostics.Add(Diagnostic.Warning("TAT001", "Fill region is empty or too small to stitch.", item.Id));
            return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, stitches), diagnostics);
        }

        var p = item.Parameters;
        var u = p.Underlay;
        var position = context.EntryPosition;
        var jumps = 0;

        if (u.EdgeRun || u.Fill)
        {
            var inset = PolygonOps.Offset(region, -u.InsetMm);
            if (inset.Rings.Count > 0)
            {
                if (u.Fill)
                {
                    var fill = new TatamiFill(inset, p.AngleDeg + 90, u.RowSpacingMm, u.StitchLengthMm, 0.5, 0, 0, p.MinStitchLengthMm, StitchLayer.Underlay);
                    jumps += fill.Emit(ref position, stitches, ct);
                }

                if (u.EdgeRun)
                {
                    jumps += EdgeRun(inset, u.StitchLengthMm, ref position, stitches);
                }
            }
        }

        var top = new TatamiFill(region, p.AngleDeg, p.RowSpacingMm, p.StitchLengthMm, p.StaggerFraction,
            p.EdgeInsetMm, p.PullCompensationMm, p.MinStitchLengthMm, StitchLayer.Top);
        jumps += top.Emit(ref position, stitches, ct);

        if (jumps > 0)
        {
            diagnostics.Add(Diagnostic.Info("TAT002",
                $"{jumps} jump(s) were needed between fill sections because no hidden travel path was found.", item.Id));
        }

        return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, stitches), diagnostics);
    }

    /// <summary>Runs around every ring of the (inset) region, starting each at its point nearest the needle.</summary>
    private static int EdgeRun(Region region, double stitchLength, ref Vec2 position, List<LogicalStitch> output)
    {
        var jumps = 0;
        var remaining = region.Rings.ToList();
        while (remaining.Count > 0)
        {
            var here = position;
            var (ring, startIndex) = remaining
                .Select(r => (Ring: r, Index: NearestVertex(r, here)))
                .MinBy(x => Vec2.Distance(x.Ring[x.Index], here));
            remaining.Remove(ring);

            var path = new List<Vec2>(ring.Count + 1);
            for (var i = 0; i <= ring.Count; i++) path.Add(ring[(startIndex + i) % ring.Count]);
            if (output.Count > 0) jumps += TatamiFill.Connect(region, position, path[0], StitchLayer.Underlay, output);
            foreach (var pt in RunSampler.Sample(path, stitchLength, 30))
            {
                output.Add(new(pt, StitchCommand.Stitch, StitchLayer.Underlay));
            }

            position = path[^1];
        }

        return jumps;
    }

    private static int NearestVertex(IReadOnlyList<Vec2> ring, Vec2 p)
    {
        var best = 0;
        for (var i = 1; i < ring.Count; i++)
        {
            if (Vec2.Distance(ring[i], p) < Vec2.Distance(ring[best], p)) best = i;
        }

        return best;
    }
}

/// <summary>
/// Core scanline fill. Works in a rotated frame where stitch rows are horizontal, then maps
/// penetrations back. Rows are grouped into sections that can be sewn back and forth
/// (boustrophedon) without leaving the region.
/// </summary>
internal sealed class TatamiFill(
    Region region,
    double angleDeg,
    double rowSpacing,
    double stitchLength,
    double staggerFraction,
    double edgeInset,
    double pullCompensation,
    double minStitchLength,
    StitchLayer layer)
{
    private const double TravelStitchLength = 2.5;

    private sealed record Row(int Index, double Y, Interval Span);

    private sealed class Section
    {
        public List<Row> Rows { get; } = [];
    }

    /// <summary>Appends the fill starting near <paramref name="position"/>; returns the number of jumps used.</summary>
    public int Emit(ref Vec2 position, List<LogicalStitch> output, CancellationToken ct)
    {
        rowSpacing = Math.Max(0.05, rowSpacing);
        stitchLength = Math.Max(0.3, stitchLength);
        var angle = angleDeg * Math.PI / 180;
        Vec2 ToLocal(Vec2 v) => v.Rotate(-angle);
        Vec2 ToWorld(Vec2 v) => v.Rotate(angle);

        var local = PolygonOps.Transform(region, ToLocal);
        var sections = BuildSections(local, ct);
        var jumps = 0;
        var here = ToLocal(position);
        var worldOutputStart = output.Count;
        var localOutput = new List<LogicalStitch>();

        while (sections.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = here;
            var (section, fromTop, leftFirst) = sections
                .SelectMany(s => new[] { (s, true, true), (s, true, false), (s, false, true), (s, false, false) })
                .MinBy(v => Vec2.Distance(StartOf(v.Item1, v.Item2, v.Item3), current));
            sections.Remove(section);

            var start = StartOf(section, fromTop, leftFirst);
            if (localOutput.Count > 0 || output.Count > 0)
            {
                jumps += Connect(local, here, start, layer, localOutput);
            }

            var rows = fromTop ? section.Rows : Enumerable.Reverse(section.Rows).ToList();
            var leftToRight = leftFirst;
            Vec2? last = null;
            foreach (var row in rows)
            {
                var pts = RowPenetrations(row, leftToRight);
                if (last is { } l && Vec2.Distance(l, pts[0]) > stitchLength)
                {
                    // Row-to-row step along a slanted edge: keep stitches short.
                    foreach (var q in RunSampler.Sample([l, pts[0]], stitchLength).Skip(1).SkipLast(1))
                    {
                        localOutput.Add(new(q, StitchCommand.Stitch, layer));
                    }
                }

                foreach (var q in pts) localOutput.Add(new(q, StitchCommand.Stitch, layer));
                last = pts[^1];
                leftToRight = !leftToRight;
            }

            here = last ?? here;
        }

        foreach (var s in localOutput) output.Add(s with { Position = ToWorld(s.Position) });
        if (output.Count > worldOutputStart) position = output[^1].Position;
        return jumps;
    }

    /// <summary>
    /// Connects two points inside a fill: a hidden travel run when the straight path stays
    /// inside the region, otherwise a jump. Returns 1 when a jump was needed.
    /// </summary>
    public static int Connect(Region region, Vec2 from, Vec2 to, StitchLayer layer, List<LogicalStitch> output)
    {
        var distance = Vec2.Distance(from, to);
        if (distance < 1e-6) return 0;
        if (distance <= TravelStitchLength || PolygonOps.SegmentInside(region, from, to))
        {
            foreach (var q in RunSampler.Sample([from, to], TravelStitchLength).Skip(1).SkipLast(1))
            {
                output.Add(new(q, StitchCommand.Travel, layer));
            }

            return 0;
        }

        output.Add(new(to, StitchCommand.Jump, layer));
        return 1;
    }

    private List<Section> BuildSections(Region local, CancellationToken ct)
    {
        var bounds = local.Bounds;
        var first = (int)Math.Ceiling(bounds.MinY / rowSpacing - 0.5);
        var last = (int)Math.Floor(bounds.MaxY / rowSpacing - 0.5);
        var sections = new List<Section>();
        var open = new List<(Section Section, Row Last)>();

        for (var k = first; k <= last; k++)
        {
            ct.ThrowIfCancellationRequested();
            // Rows sit on a world-anchored grid so neighbouring objects line up.
            var y = (k + 0.5) * rowSpacing;
            var rows = new List<Row>();
            foreach (var span in PolygonOps.Scanline(local, y))
            {
                var x0 = span.X0 + edgeInset - pullCompensation;
                var x1 = span.X1 - edgeInset + pullCompensation;
                if (x1 - x0 >= Math.Max(minStitchLength, 0.2)) rows.Add(new Row(k, y, new Interval(x0, x1)));
            }

            var nextOpen = new List<(Section, Row)>();
            foreach (var row in rows)
            {
                var candidates = open.Where(o => o.Last.Index == k - 1 && Overlaps(o.Last.Span, row.Span)).ToList();
                if (candidates.Count == 1 && rows.Count(r => Overlaps(candidates[0].Last.Span, r.Span)) == 1)
                {
                    candidates[0].Section.Rows.Add(row);
                    nextOpen.Add((candidates[0].Section, row));
                }
                else
                {
                    var section = new Section();
                    section.Rows.Add(row);
                    sections.Add(section);
                    nextOpen.Add((section, row));
                }
            }

            open = nextOpen;
        }

        return sections;
    }

    private static bool Overlaps(Interval a, Interval b) => a.X0 < b.X1 && b.X0 < a.X1;

    private Vec2 StartOf(Section s, bool fromTop, bool leftFirst)
    {
        var row = fromTop ? s.Rows[0] : s.Rows[^1];
        return new Vec2(leftFirst ? row.Span.X0 : row.Span.X1, row.Y);
    }

    /// <summary>
    /// Penetrations for one row. Inner penetrations sit on a grid shifted by the stagger
    /// fraction per row index, so the brick pattern is continuous across sections.
    /// </summary>
    private List<Vec2> RowPenetrations(Row row, bool leftToRight)
    {
        var (x0, x1) = (row.Span.X0, row.Span.X1);
        var phase = (row.Index * staggerFraction % 1 + 1) % 1 * stitchLength;
        var xs = new List<double> { x0 };
        var m = Math.Ceiling((x0 + minStitchLength - phase) / stitchLength);
        for (var x = phase + m * stitchLength; x < x1 - minStitchLength; x += stitchLength)
        {
            if (x - xs[^1] >= minStitchLength) xs.Add(x);
        }

        xs.Add(x1);
        if (!leftToRight) xs.Reverse();
        return xs.Select(x => new Vec2(x, row.Y)).ToList();
    }
}
