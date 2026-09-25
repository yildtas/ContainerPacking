using Embroidery.Core.Diagnostics;
using Embroidery.Core.StitchPlan;

namespace Embroidery.Application.Preview;

/// <summary>
/// Compact preview of a plan for the UI. Coordinates are flattened [x0, y0, x1, y1, ...]
/// in millimetres; <see cref="PreviewBlock.Commands"/> holds one <see cref="StitchCommand"/> per point.
/// </summary>
public sealed record PreviewDto(
    long Revision,
    IReadOnlyList<PreviewThread> Threads,
    IReadOnlyList<PreviewBlock> Blocks,
    PreviewStatistics Statistics,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed record PreviewThread(string Name, string Color);

public sealed record PreviewBlock(
    Guid ObjectId,
    BlockKind Kind,
    int ThreadIndex,
    float[] Points,
    int[] Commands,
    int[] Layers);

public sealed record PreviewStatistics(
    int StitchCount,
    int JumpCount,
    int TrimCount,
    int ColorChangeCount,
    double WidthMm,
    double HeightMm,
    double ThreadLengthM,
    double MinX,
    double MinY);

public static class PreviewBuilder
{
    public static PreviewDto Build(long revision, Core.Model.Design design, LogicalStitchPlan plan)
    {
        var blocks = plan.Blocks.Select(b =>
        {
            var pts = new float[b.Stitches.Count * 2];
            var cmds = new int[b.Stitches.Count];
            var layers = new int[b.Stitches.Count];
            for (var i = 0; i < b.Stitches.Count; i++)
            {
                var s = b.Stitches[i];
                pts[2 * i] = (float)s.Position.X;
                pts[2 * i + 1] = (float)s.Position.Y;
                cmds[i] = (int)s.Command;
                layers[i] = (int)s.Layer;
            }

            return new PreviewBlock(b.ObjectId, b.Kind, b.ThreadIndex, pts, cmds, layers);
        }).ToList();

        var st = plan.ComputeStatistics();
        var stats = new PreviewStatistics(st.StitchCount, st.JumpCount, st.TrimCount, st.ColorChangeCount,
            st.Bounds.Width, st.Bounds.Height, st.ThreadLengthMm / 1000,
            st.Bounds.IsEmpty ? 0 : st.Bounds.MinX, st.Bounds.IsEmpty ? 0 : st.Bounds.MinY);

        return new PreviewDto(revision,
            design.Threads.Select(t => new PreviewThread(t.Name, t.ColorHex)).ToList(),
            blocks, stats, plan.Diagnostics);
    }
}
