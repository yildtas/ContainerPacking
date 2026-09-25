using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;

namespace Embroidery.StitchEngine.Generators;

/// <summary>
/// Running stitch along a path. Entry candidate 1 sews the path backwards.
/// Codes: RUN001 path too short.
/// </summary>
public sealed class RunGenerator : IStitchGenerator<RunObject>
{
    public GenerationResult<LogicalStitchBlock> Generate(RunObject item, GenerationContext context, CancellationToken ct = default)
    {
        var diagnostics = new List<Diagnostic>();
        IReadOnlyList<Vec2> path = item.Path;
        if (context.EntryCandidate == 1) path = path.Reverse().ToArray();

        var p = item.Parameters;
        var points = RunSampler.Sample(path, p.StitchLengthMm, p.CornerAngleDeg);
        if (points.Count < 2)
        {
            diagnostics.Add(Diagnostic.Warning("RUN001", "Run path is too short to stitch.", item.Id));
            return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, []), diagnostics);
        }

        var repeats = Math.Max(1, p.Repeats | 1); // force odd so the run ends at the path end
        var stitches = new List<LogicalStitch>(points.Count * repeats) { new(points[0], StitchCommand.Stitch) };
        for (var i = 1; i < points.Count; i++)
        {
            for (var r = 1; r < repeats; r += 2)
            {
                stitches.Add(new(points[i], StitchCommand.Stitch));
                stitches.Add(new(points[i - 1], StitchCommand.Stitch));
            }

            stitches.Add(new(points[i], StitchCommand.Stitch));
        }

        return new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, stitches), diagnostics);
    }
}
