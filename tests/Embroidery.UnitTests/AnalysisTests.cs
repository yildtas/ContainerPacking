using Embroidery.Application.Analysis;
using Embroidery.Application.Calibration;
using Embroidery.Application.Projects;
using Embroidery.Core.Diagnostics;
using Embroidery.Machine;

namespace Embroidery.UnitTests;

public class AnalysisTests
{
    /// <summary>A 4 mm wide satin with 0.3 mm same-rail spacing, a trim, then a 10-stitch run.</summary>
    private static EncodedStitchPlan Synthetic()
    {
        var s = new List<EncodedStitch>();
        for (var i = 0; i <= 40; i++)
        {
            // Rails at y=0 and y=40 units; each rail advances 3 units (0.3 mm) per penetration.
            s.Add(new EncodedStitch(i * 3, 0, EncodedCommand.Stitch));
            s.Add(new EncodedStitch(i * 3 + 1, 40, EncodedCommand.Stitch));
        }

        var x = s[^1].X;
        s.Add(new EncodedStitch(x + 2, 42, EncodedCommand.Jump));
        s.Add(new EncodedStitch(x - 2, 38, EncodedCommand.Jump));
        s.Add(new EncodedStitch(x, 40, EncodedCommand.Jump));
        s.Add(new EncodedStitch(x + 300, 400, EncodedCommand.Jump));
        for (var i = 1; i <= 10; i++) s.Add(new EncodedStitch(x + 300 + i * 25, 400, EncodedCommand.Stitch));
        s.Add(new EncodedStitch(s[^1].X, 400, EncodedCommand.End));
        return new EncodedStitchPlan("t", s);
    }

    [Fact]
    public void Metrics_recover_satin_width_density_trims_and_runs()
    {
        var m = StitchMetrics.From(Synthetic());
        Assert.Equal(4, m.Jumps);
        Assert.Equal(1, m.JumpRuns);
        Assert.Equal(1, m.InferredTrims);
        Assert.Equal(4.0, m.SatinThrowMm!.P50, 1);
        Assert.Equal(0.3, m.SatinSameRailSpacingMm!.P50, 2);
        Assert.Equal(2.5, m.RunningStitchMm!.P50, 2);
        Assert.InRange(m.RunningShare, 0.1, 0.13); // 10 of 91 moves
    }

    [Fact]
    public void Calibration_sheet_encodes_cleanly_and_fits_the_hoop()
    {
        var (design, legend) = CalibrationSheet.Build();
        var (encoded, plan) = new ProjectService().Encode(design);
        Assert.DoesNotContain(plan.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal(design.Objects.Count, legend.Count);
        var m = StitchMetrics.From(encoded);
        Assert.True(m.WidthMm <= design.Hoop.WidthMm && m.HeightMm <= design.Hoop.HeightMm);
        var md = CalibrationSheet.LegendMarkdown(legend);
        Assert.Contains("| A1.1 |", md);
        Assert.Contains("| E3 |", md);
        Assert.Contains("| F3 |", md);
    }

    [Fact]
    public void Svg_preview_draws_stitches_and_dashed_jumps()
    {
        var svg = StitchSvgRenderer.Render(Synthetic(), ["#D4A53C"], "#7A101C");
        Assert.StartsWith("<svg", svg);
        Assert.Contains("stroke=\"#D4A53C\"", svg);
        Assert.Contains("stroke-dasharray", svg);
        Assert.Contains("fill=\"#7A101C\"", svg);
    }
}
