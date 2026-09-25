using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;

namespace Embroidery.StitchEngine.Quality;

public sealed record QualityOptions
{
    public double MinStitchLengthMm { get; init; } = 0.3;
    public double DuplicateToleranceMm { get; init; } = 0.01;

    /// <summary>Longest stitch the target format can take in one record (DST: 12.1 mm).</summary>
    public double LongStitchMm { get; init; } = 12.1;
}

/// <summary>
/// Checks the plan before encoding. Any change it makes (duplicate removal) is reported,
/// never silent. Codes: Q001 short stitches, Q002 duplicates removed, Q003 long stitches,
/// Q004 design larger than hoop.
/// </summary>
public static class QualityPass
{
    public static LogicalStitchPlan Run(LogicalStitchPlan plan, Design design, QualityOptions? options = null)
    {
        options ??= new QualityOptions();
        var diagnostics = new List<Diagnostic>(plan.Diagnostics);
        var blocks = new List<LogicalStitchBlock>(plan.Blocks.Count);
        Vec2? prev = null;

        foreach (var block in plan.Blocks)
        {
            if (block.Kind == BlockKind.Connector)
            {
                blocks.Add(block);
                foreach (var s in block.Stitches)
                {
                    if (s.Command is StitchCommand.Jump or StitchCommand.Stitch or StitchCommand.Travel) prev = s.Position;
                }

                continue;
            }

            int duplicates = 0, shortCount = 0, longCount = 0;
            var kept = new List<LogicalStitch>(block.Stitches.Count);
            foreach (var s in block.Stitches)
            {
                if (s.Command is StitchCommand.Stitch or StitchCommand.Travel && prev is { } p)
                {
                    var d = Vec2.Distance(p, s.Position);
                    if (d <= options.DuplicateToleranceMm)
                    {
                        // Landing on the connector's jump target is expected, not a defect.
                        if (kept.Count > 0) duplicates++;
                        continue;
                    }

                    if (d < options.MinStitchLengthMm) shortCount++;
                    if (d > options.LongStitchMm) longCount++;
                }

                kept.Add(s);
                if (s.Command is StitchCommand.Jump or StitchCommand.Stitch or StitchCommand.Travel) prev = s.Position;
            }

            if (duplicates > 0)
            {
                diagnostics.Add(Diagnostic.Info("Q002", $"{duplicates} duplicate penetration(s) removed.", block.ObjectId));
            }

            if (shortCount > 0)
            {
                diagnostics.Add(Diagnostic.Info("Q001",
                    FormattableString.Invariant($"{shortCount} stitch(es) shorter than {options.MinStitchLengthMm} mm."), block.ObjectId));
            }

            if (longCount > 0)
            {
                diagnostics.Add(Diagnostic.Info("Q003",
                    FormattableString.Invariant($"{longCount} stitch(es) longer than {options.LongStitchMm} mm will be split by the machine encoder."), block.ObjectId));
            }

            blocks.Add(block with { Stitches = kept });
        }

        var result = new LogicalStitchPlan(blocks, diagnostics);
        var bounds = result.ComputeStatistics().Bounds;
        if (!bounds.IsEmpty && (bounds.Width > design.Hoop.WidthMm || bounds.Height > design.Hoop.HeightMm))
        {
            diagnostics.Add(Diagnostic.Error("Q004", FormattableString.Invariant(
                $"Design is {bounds.Width:0.0} × {bounds.Height:0.0} mm but hoop '{design.Hoop.Name}' is {design.Hoop.WidthMm:0} × {design.Hoop.HeightMm:0} mm.")));
        }

        return result;
    }
}
