using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Geometry;

namespace Embroidery.StitchEngine.Generators;

/// <summary>
/// Twisted-cord border. Works in the band's own frame (s along the path, t across it):
/// strand k is a lens-shaped satin column from (k·pitch, −w/2) to (k·pitch + L, +w/2) for an S twist.
/// A centre run is sewn first (start → end), then the strands from the far end back, alternating
/// their direction so that each hop to the next strand is one pitch along an edge and lies under
/// the strand sewn next. The object therefore starts and ends at the same end of the path.
/// Entry candidate 1 starts from the other end. Codes: ROPE001 invalid geometry, ROPE002 tight curve.
/// </summary>
public sealed class RopeGenerator : IStitchGenerator<RopeObject>
{
    public GenerationResult<LogicalStitchBlock> Generate(RopeObject item, GenerationContext context, CancellationToken ct = default)
    {
        var diagnostics = new List<Diagnostic>();
        var empty = new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, []);
        IReadOnlyList<Vec2> source = item.Path;
        if (source.Count < 2 || item.WidthMm <= 0)
        {
            diagnostics.Add(Diagnostic.Error("ROPE001", "A rope needs a path of at least two points and a positive width.", item.Id));
            return new(empty, diagnostics);
        }

        if (context.EntryCandidate == 1) source = source.Reverse().ToArray();
        var path = new ArcLengthPath(source);
        var p = item.Parameters;
        var w = item.WidthMm;
        var pitch = Math.Max(0.5, p.PitchMm);
        var span = Math.Max(0.5, p.StrandLengthMm);
        if (path.Length < span)
        {
            diagnostics.Add(Diagnostic.Error("ROPE001", "The rope path is shorter than one strand.", item.Id));
            return new(empty, diagnostics);
        }

        // Frame: point and left-hand normal at arc length s (central differences over a small window).
        Vec2 Normal(double s)
        {
            var d = path.PointAt(Math.Min(path.Length, s + 0.25)) - path.PointAt(Math.Max(0, s - 0.25));
            return d.Normalized().Perpendicular;
        }

        Vec2 World(double s, double t) => path.PointAt(Math.Clamp(s, 0, path.Length)) + Normal(Math.Clamp(s, 0, path.Length)) * t;

        var sign = p.Twist == TwistDirection.S ? 1.0 : -1.0;
        var strandDir = new Vec2(span, sign * w).Normalized();          // in (s, t) coordinates
        var across = strandDir.Perpendicular;                          // strand thickness direction
        var gapPerp = pitch * Math.Abs(Vec2.Cross(new Vec2(1, 0), strandDir)); // distance between strand axes
        var thickness = gapPerp * Math.Max(0.5, p.OverlapFactor);
        var count = (int)Math.Floor((path.Length - span) / pitch) + 1;

        var stitches = new List<LogicalStitch>();
        if (p.CenterUnderlay)
        {
            foreach (var q in RunSampler.Sample(path.Points, 2.0, 20))
            {
                stitches.Add(new(q, StitchCommand.Stitch, StitchLayer.Underlay));
            }
        }

        var satin = new SatinParameters
        {
            SpacingMm = p.SpacingMm,
            PullCompensationMm = p.PullCompensationMm,
            ShortStitch = ShortStitchMode.None,
            MaxWidthMm = 20,
            Underlay = new SatinUnderlay { CenterWalk = false },
        };

        const int samples = 24;
        var folds = 0;
        for (var n = 0; n < count; n++)
        {
            ct.ThrowIfCancellationRequested();
            var k = count - 1 - n;                     // far end first
            var s0 = k * pitch;
            var a = new Vec2[samples + 1];
            var b = new Vec2[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var u = (double)i / samples;
                // Lens: thin where the strand meets the band edges, full thickness in the middle.
                var half = thickness * Math.Max(0.12, Math.Sin(Math.PI * u)) / 2;
                var c = new Vec2(s0 + span * u, sign * (-w / 2 + w * u));
                var la = c - across * half;
                var lb = c + across * half;
                a[i] = World(la.X, la.Y);
                b[i] = World(lb.X, lb.Y);
            }

            var ladder = SatinLadder.FromPairs(a, b);
            if (n % 2 == 1) ladder = ladder.Reversed();
            if (ladder.MaxWidth > thickness * 2.5) folds++;
            SatinGenerator.AddTop(ladder, satin, stitches);
        }

        if (folds > 0)
        {
            diagnostics.Add(Diagnostic.Warning("ROPE002",
                $"The path bends too tightly for a {w:0.#} mm rope in {folds} strand(s); strands are distorted there.", item.Id));
        }

        return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, stitches), diagnostics);
    }
}
