using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Geometry;
using Embroidery.StitchEngine;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.UnitTests;

public class GeneratorTests
{
    private static readonly GenerationContext Start = GenerationContext.Default;

    private static List<Vec2> Penetrations(LogicalStitchBlock block, StitchLayer? layer = null) =>
        block.Stitches.Where(s => s.Command is StitchCommand.Stitch && (layer is null || s.Layer == layer)).Select(s => s.Position).ToList();

    [Fact]
    public void Run_stitches_never_exceed_stitch_length_and_hit_corners()
    {
        var run = new RunObject
        {
            Id = Guid.NewGuid(), Name = "r", ThreadIndex = 0,
            Path = [new(0, 0), new(10, 0), new(10, 7)],
            Parameters = new RunParameters { StitchLengthMm = 2.5 },
        };

        var pts = Penetrations(new RunGenerator().Generate(run, Start).Value);
        for (var i = 1; i < pts.Count; i++) Assert.True(Vec2.Distance(pts[i - 1], pts[i]) <= 2.5 + 1e-9);
        Assert.Contains(new Vec2(10, 0), pts);
        Assert.Equal(new Vec2(0, 0), pts[0]);
        Assert.Equal(new Vec2(10, 7), pts[^1]);
    }

    [Fact]
    public void Run_entry_candidate_one_reverses_the_path()
    {
        var run = new RunObject { Id = Guid.NewGuid(), Name = "r", ThreadIndex = 0, Path = [new(0, 0), new(10, 0)] };
        var pts = Penetrations(new RunGenerator().Generate(run, new GenerationContext(1, new Vec2(10, 0))).Value);
        Assert.Equal(new Vec2(10, 0), pts[0]);
        Assert.Equal(new Vec2(0, 0), pts[^1]);
    }

    [Fact]
    public void Triple_run_goes_back_and_forth_on_every_stitch()
    {
        var run = new RunObject
        {
            Id = Guid.NewGuid(), Name = "r", ThreadIndex = 0, Path = [new(0, 0), new(5, 0)],
            Parameters = new RunParameters { StitchLengthMm = 2.5, Repeats = 3 },
        };

        var pts = Penetrations(new RunGenerator().Generate(run, Start).Value);
        // 3 sample points → 2 stitches, each sewn 3 times: 1 + 2*3 penetrations.
        Assert.Equal(7, pts.Count);
        Assert.Equal(new Vec2(5, 0), pts[^1]);
    }

    private static SatinObject Column(double length, double width, SatinParameters? p = null) => new()
    {
        Id = Guid.NewGuid(), Name = "s", ThreadIndex = 0,
        RailA = [new(0, 0), new(length, 0)],
        RailB = [new(0, width), new(length, width)],
        Parameters = p ?? new SatinParameters { Underlay = new SatinUnderlay { CenterWalk = false } },
    };

    [Fact]
    public void Satin_alternates_between_rails_with_pull_compensation()
    {
        var satin = Column(20, 4, new SatinParameters { SpacingMm = 0.4, PullCompensationMm = 0.2, Underlay = new SatinUnderlay { CenterWalk = false } });
        var pts = Penetrations(new SatinGenerator().Generate(satin, Start).Value, StitchLayer.Top);

        Assert.Equal(2 * (20 / 0.4 + 1), pts.Count);
        for (var i = 0; i < pts.Count; i++)
        {
            Assert.Equal(i % 2 == 0 ? -0.1 : 4.1, pts[i].Y, 9);
        }

        Assert.Equal(0, pts[0].X, 9);
        Assert.Equal(20, pts[^1].X, 9);
    }

    [Fact]
    public void Satin_with_opposite_rails_is_fixed_and_reported()
    {
        var satin = Column(20, 4) with { RailB = [new(20, 4), new(0, 4)] };
        var result = new SatinGenerator().Generate(satin, Start);
        Assert.Contains(result.Diagnostics, d => d.Code == "SAT002");
        var pts = Penetrations(result.Value, StitchLayer.Top);
        Assert.Equal(0, pts[1].X, 9); // first throw goes straight across
    }

    [Fact]
    public void Wide_satin_is_split_and_very_wide_satin_is_flagged()
    {
        var p = new SatinParameters { MaxWidthMm = 8, PullCompensationMm = 0, Underlay = new SatinUnderlay { CenterWalk = false } };
        var result = new SatinGenerator().Generate(Column(10, 14, p), Start);
        var pts = Penetrations(result.Value, StitchLayer.Top);
        for (var i = 1; i < pts.Count; i++) Assert.True(Vec2.Distance(pts[i - 1], pts[i]) <= 8 + 1e-9);
        Assert.Contains(result.Diagnostics, d => d.Code == "SAT003");
    }

    [Fact]
    public void Satin_underlay_comes_first_and_ends_near_the_column_start()
    {
        var p = new SatinParameters { Underlay = new SatinUnderlay { CenterWalk = true, EdgeWalk = true, ZigZag = true } };
        var block = new SatinGenerator().Generate(Column(30, 5, p), Start).Value;
        var firstTop = block.Stitches.ToList().FindIndex(s => s.Layer == StitchLayer.Top);
        Assert.True(firstTop > 0);
        Assert.All(block.Stitches.Take(firstTop), s => Assert.Equal(StitchLayer.Underlay, s.Layer));
        Assert.All(block.Stitches.Skip(firstTop), s => Assert.Equal(StitchLayer.Top, s.Layer));
        Assert.True(Vec2.Distance(block.Stitches[firstTop - 1].Position, block.Stitches[firstTop].Position) < 5.5);
    }

    [Fact]
    public void Satin_push_compensation_shortens_the_column()
    {
        var p = new SatinParameters { PushCompensationMm = 1, Underlay = new SatinUnderlay { CenterWalk = false } };
        var pts = Penetrations(new SatinGenerator().Generate(Column(20, 4, p), Start).Value);
        Assert.Equal(1, pts.Min(q => q.X), 6);
        Assert.Equal(19, pts.Max(q => q.X), 6);
    }

    private static TatamiObject Fill(Region region, TatamiParameters? p = null) => new()
    {
        Id = Guid.NewGuid(), Name = "t", ThreadIndex = 0, Region = region,
        Parameters = p ?? new TatamiParameters { AngleDeg = 0, PullCompensationMm = 0, Underlay = new TatamiUnderlay { EdgeRun = false, Fill = false } },
    };

    private static Region Donut() => new(
    [
        new Vec2[] { new(0, 0), new(30, 0), new(30, 30), new(0, 30) },
        new Vec2[] { new(10, 10), new(10, 20), new(20, 20), new(20, 10) },
    ]);

    [Fact]
    public void Tatami_penetrations_stay_inside_and_avoid_holes()
    {
        var region = Donut();
        var block = new TatamiGenerator().Generate(Fill(region), Start).Value;
        var pts = Penetrations(block);
        Assert.NotEmpty(pts);
        var tolerant = PolygonOps.Offset(region, 0.01);
        Assert.All(pts, q => Assert.True(PolygonOps.Contains(tolerant, q), $"{q} is outside the region"));
        Assert.DoesNotContain(pts, q => q.X is > 10.1 and < 19.9 && q.Y is > 10.1 and < 19.9);
    }

    [Fact]
    public void Tatami_stitches_respect_stitch_length_and_row_spacing()
    {
        var p = new TatamiParameters { AngleDeg = 0, RowSpacingMm = 0.5, StitchLengthMm = 3, PullCompensationMm = 0, Underlay = new TatamiUnderlay { EdgeRun = false, Fill = false } };
        var block = new TatamiGenerator().Generate(Fill(new Region([new Vec2[] { new(0, 0), new(20, 0), new(20, 10), new(0, 10) }]), p), Start).Value;
        var pts = Penetrations(block);
        for (var i = 1; i < pts.Count; i++) Assert.True(Vec2.Distance(pts[i - 1], pts[i]) <= 3 + 1e-6);
        var rows = pts.Select(q => Math.Round(q.Y, 6)).Distinct().Count();
        Assert.Equal(20, rows); // 10 mm / 0.5 mm
    }

    [Fact]
    public void Tatami_rows_are_staggered()
    {
        var p = new TatamiParameters { AngleDeg = 0, RowSpacingMm = 1, StitchLengthMm = 3, StaggerFraction = 1.0 / 3, PullCompensationMm = 0, MinStitchLengthMm = 0.5, Underlay = new TatamiUnderlay { EdgeRun = false, Fill = false } };
        var pts = Penetrations(new TatamiGenerator().Generate(Fill(new Region([new Vec2[] { new(0, 0), new(30, 0), new(30, 3), new(0, 3) }]), p), Start).Value);
        double[] Inner(double y) => pts.Where(q => Math.Abs(q.Y - y) < 1e-6 && q.X is > 0.01 and < 29.99).Select(q => q.X).OrderBy(x => x).ToArray();
        var r0 = Inner(0.5);
        var r1 = Inner(1.5);
        Assert.Equal(0, r0[0] % 3, 6);
        Assert.Equal(1, r1[0] % 3, 6);
    }

    [Fact]
    public void Tatami_circle_with_underlay_has_no_micro_stitches()
    {
        var circle = Embroidery.Geometry.Svg.SvgPathParser.Parse("M5,20 A15,15 0 1 0 35,20 A15,15 0 1 0 5,20 Z", Matrix2D.Identity);
        var item = Fill(new Region([circle[0].Points]), new TatamiParameters());
        var block = new TatamiGenerator().Generate(item, Start).Value;
        var moves = block.Stitches.Where(s => s.Command is StitchCommand.Stitch or StitchCommand.Travel).Select(s => s.Position).ToList();
        var micro = moves.Zip(moves.Skip(1), Vec2.Distance).Count(d => d < 0.2);
        Assert.Equal(0, micro);
    }

    [Fact]
    public void Convex_fill_with_underlay_and_pull_compensation_needs_no_jumps()
    {
        var circle = Embroidery.Geometry.Svg.SvgPathParser.Parse("M8,30 A22,22 0 1 0 52,30 A22,22 0 1 0 8,30 Z", Matrix2D.Identity);
        var item = Fill(new Region([circle[0].Points]), new TatamiParameters { PullCompensationMm = 0.2 });
        var result = new TatamiGenerator().Generate(item, Start);
        Assert.DoesNotContain(result.Value.Stitches, s => s.Command == StitchCommand.Jump);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "TAT002");
    }

    [Fact]
    public void Concave_fill_travels_along_the_boundary_instead_of_jumping()
    {
        // A "U": rows split into two arms, so sections must be connected around the bend.
        var u = new Region([new Vec2[] { new(0, 0), new(30, 0), new(30, 30), new(20, 30), new(20, 10), new(10, 10), new(10, 30), new(0, 30) }]);
        var star = Embroidery.Geometry.Svg.SvgPathParser.Parse("M30 14 L34 25 L46 25 L36 32 L40 44 L30 36 L20 44 L24 32 L14 25 L26 25 Z", Matrix2D.Identity);
        foreach (var region in new[] { u, new Region([star[0].Points]) })
        {
            foreach (var angle in new[] { 0.0, 45, 90 })
            {
                var result = new TatamiGenerator().Generate(Fill(region, new TatamiParameters { AngleDeg = angle }), Start);
                Assert.DoesNotContain(result.Diagnostics, d => d.Code == "TAT002");
                var tolerant = PolygonOps.Offset(region, 0.3);
                Assert.All(result.Value.Stitches, s => Assert.True(PolygonOps.Contains(tolerant, s.Position), $"{s.Position} outside at {angle}°"));
            }
        }
    }

    /// <summary>
    /// Regression from the 2026-09-23 source review: bastidor accepted the short connector
    /// (10,1)→(10,3) across the notch because it only checked distance. So did we.
    /// </summary>
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.2, false)]
    [InlineData(0.2, true)]
    public void Short_row_connectors_never_cross_a_notch(double pull, bool underlay)
    {
        var region = new Region([new Vec2[] { new(0, 0), new(10, 0), new(10, 1.5), new(8, 2), new(10, 2.5), new(10, 6), new(0, 6) }]);
        var p = new TatamiParameters
        {
            AngleDeg = 0, RowSpacingMm = 2, StitchLengthMm = 3, PullCompensationMm = pull,
            Underlay = new TatamiUnderlay { EdgeRun = underlay, Fill = underlay },
        };
        var stitches = new TatamiGenerator().Generate(Fill(region, p), Start).Value.Stitches;
        var allowed = PolygonOps.Offset(region, pull + 0.06);
        for (var i = 1; i < stitches.Count; i++)
        {
            if (stitches[i].Command is not (StitchCommand.Stitch or StitchCommand.Travel)) continue;
            var (a, b) = (stitches[i - 1].Position, stitches[i].Position);
            Assert.True(PolygonOps.SegmentInside(allowed, a, b, 0.05), $"needle-down {a}->{b} leaves the shape");
        }
    }

    [Fact]
    public void Satin_underlay_does_not_repeat_turning_points()
    {
        var p = new SatinParameters { Underlay = new SatinUnderlay { CenterWalk = true, EdgeWalk = true } };
        var pts = Penetrations(new SatinGenerator().Generate(Column(20, 4, p), Start).Value);
        Assert.DoesNotContain(pts.Zip(pts.Skip(1)), pair => pair.First.ApproximatelyEquals(pair.Second, 1e-6));
    }

    [Fact]
    public void Tatami_is_deterministic()
    {
        var item = Fill(Donut(), new TatamiParameters { AngleDeg = 30 });
        var a = new TatamiGenerator().Generate(item, Start).Value.Stitches;
        var b = new TatamiGenerator().Generate(item, Start).Value.Stitches;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Tatami_underlay_precedes_top_and_empty_region_is_reported()
    {
        var item = Fill(Donut(), new TatamiParameters());
        var block = new TatamiGenerator().Generate(item, Start).Value;
        var firstTop = block.Stitches.ToList().FindIndex(s => s.Layer == StitchLayer.Top);
        Assert.True(firstTop > 0);
        Assert.All(block.Stitches.Take(firstTop), s => Assert.Equal(StitchLayer.Underlay, s.Layer));

        var empty = new TatamiGenerator().Generate(Fill(new Region([])), Start);
        Assert.Contains(empty.Diagnostics, d => d.Code == "TAT001");
        Assert.Empty(empty.Value.Stitches);
    }

    [Fact]
    public void Tatami_on_disjoint_parts_uses_jumps_and_reports_them()
    {
        var region = new Region(
        [
            new Vec2[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10) },
            new Vec2[] { new(30, 0), new(40, 0), new(40, 10), new(30, 10) },
        ]);
        var result = new TatamiGenerator().Generate(Fill(region), Start);
        Assert.Contains(result.Value.Stitches, s => s.Command == StitchCommand.Jump);
        Assert.Contains(result.Diagnostics, d => d.Code == "TAT002");
    }
}
