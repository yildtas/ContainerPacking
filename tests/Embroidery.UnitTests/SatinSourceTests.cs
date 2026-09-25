using Embroidery.Application.Import;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Geometry;
using Embroidery.Geometry.Svg;
using Embroidery.StitchEngine;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.UnitTests;

public class SatinSourceTests
{
    private static readonly SatinParameters NoUnderlay = new() { PullCompensationMm = 0, ShortStitch = ShortStitchMode.None, Underlay = new SatinUnderlay { CenterWalk = false } };

    private static SatinObject Stroke(IReadOnlyList<Vec2> line, double width, SatinParameters? p = null, double taper = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "s", ThreadIndex = 0, Source = SatinSource.Stroke,
        Centerline = line, WidthMm = width, StartTaperMm = taper, EndTaperMm = taper, Parameters = p ?? NoUnderlay,
    };

    private static List<Vec2> Top(SatinObject s) =>
        new SatinGenerator().Generate(s, GenerationContext.Default).Value.Stitches
            .Where(x => x.Layer == StitchLayer.Top).Select(x => x.Position).ToList();

    private static List<Vec2> Arc(double radius, double fromDeg, double toDeg, int n = 180) =>
        Enumerable.Range(0, n + 1).Select(i =>
        {
            var t = (fromDeg + (toDeg - fromDeg) * i / n) * Math.PI / 180;
            return new Vec2(radius * Math.Cos(t), radius * Math.Sin(t));
        }).ToList();

    [Fact]
    public void Stroke_satin_throws_span_the_width_plus_compensation()
    {
        var p = NoUnderlay with { PullCompensationMm = 0.3 };
        var pts = Top(Stroke([new(0, 0), new(20, 0)], 4, p));
        Assert.Equal(2 * 51, pts.Count);
        for (var i = 0; i + 1 < pts.Count; i += 2) Assert.Equal(4.3, Vec2.Distance(pts[i], pts[i + 1]), 6);
    }

    [Fact]
    public void Tapered_ends_start_narrow_and_reach_full_width()
    {
        var pts = Top(Stroke([new(0, 0), new(30, 0)], 4, taper: 5));
        var widths = Enumerable.Range(0, pts.Count / 2).Select(i => Vec2.Distance(pts[2 * i], pts[2 * i + 1])).ToList();
        Assert.True(widths[0] < 0.5, $"start width {widths[0]}");
        Assert.True(widths[^1] < 0.5, $"end width {widths[^1]}");
        Assert.Equal(4, widths[widths.Count / 2], 3);
    }

    [Fact]
    public void Curved_satin_keeps_the_requested_spacing_on_the_outer_rail()
    {
        // Radius 10, width 4: outer edge r=12, inner r=8.
        var pts = Top(Stroke(Arc(10, 0, 180), 4, NoUnderlay with { SpacingMm = 0.4 }));
        var outer = new List<Vec2>();
        var inner = new List<Vec2>();
        foreach (var q in pts) (q.Length > 10 ? outer : inner).Add(q);
        var outerSteps = outer.Zip(outer.Skip(1), Vec2.Distance).ToList();
        var innerSteps = inner.Zip(inner.Skip(1), Vec2.Distance).ToList();
        Assert.All(outerSteps, d => Assert.InRange(d, 0.36, 0.41));
        Assert.True(innerSteps.Average() < 0.3, $"inner spacing {innerSteps.Average()}");
    }

    [Fact]
    public void Too_tight_curve_for_the_width_is_reported()
    {
        var result = new SatinGenerator().Generate(Stroke(Arc(2, 0, 180), 6), GenerationContext.Default);
        Assert.Contains(result.Diagnostics, d => d.Code == "SAT004");
    }

    [Fact]
    public void Sharp_corner_splits_the_column_and_covers_the_outside()
    {
        var corner = Stroke([new(0, 0), new(20, 0), new(20, 20)], 4, NoUnderlay);
        var result = new SatinGenerator().Generate(corner, GenerationContext.Default);
        Assert.Contains(result.Diagnostics, d => d.Code == "SAT006");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "SAT004");
        // The outer corner (22, -2) is reached by the lapped first piece.
        Assert.Contains(result.Value.Stitches, s => s.Position.X > 21.5 && s.Position.Y < -1.5);

        var unsplit = new SatinGenerator().Generate(corner with { Parameters = NoUnderlay with { CornerSplitAngleDeg = 180 } }, GenerationContext.Default);
        Assert.Contains(unsplit.Diagnostics, d => d.Code == "SAT004");
    }

    [Fact]
    public void Miter_corner_pieces_meet_on_the_bisector_and_reach_the_outer_corner()
    {
        var p = NoUnderlay with { CornerStyle = CornerStyle.Miter };
        var result = new SatinGenerator().Generate(Stroke([new(0, 0), new(20, 0), new(20, 20)], 4, p), GenerationContext.Default);
        Assert.Contains(result.Diagnostics, d => d.Code == "SAT006" && d.Message.Contains("miter"));
        var pts = result.Value.Stitches.Select(s => s.Position).ToList();
        // Nothing beyond the square outer corner (22, -2); the outer corner itself is reached.
        Assert.All(pts, q => Assert.True(q.X <= 22.01 && q.Y >= -2.01, $"{q}"));
        Assert.Contains(pts, q => Vec2.Distance(q, new Vec2(22, -2)) < 0.3);
        // First leg stays on its side of the miter line through (20,0) with normal (1,1)/√2.
        var firstLeg = pts.TakeWhile(q => q.Y < 1).ToList();
        Assert.All(firstLeg, q => Assert.True((q.X - 20) + q.Y <= 0.01, $"{q} crosses the miter line"));
    }

    [Fact]
    public void Cap_corner_stops_both_pieces_at_the_corner()
    {
        // A 160° hairpin: auto chooses cap.
        var angle = 160 * Math.PI / 180;
        var tip = new Vec2(20, 0);
        var back = tip + new Vec2(Math.Cos(angle), Math.Sin(angle)) * 20;
        var result = new SatinGenerator().Generate(Stroke([new(0, 0), tip, back], 3), GenerationContext.Default);
        Assert.Contains(result.Diagnostics, d => d.Code == "SAT006" && d.Message.Contains("cap"));
        Assert.All(result.Value.Stitches, s => Assert.True(s.Position.X <= 21.6, $"{s.Position} runs past the tip"));
    }

    [Fact]
    public void Auto_uses_miter_for_right_angles_and_lap_for_steeper_turns()
    {
        var right = new SatinGenerator().Generate(Stroke([new(0, 0), new(20, 0), new(20, 20)], 4), GenerationContext.Default);
        Assert.Contains(right.Diagnostics, d => d.Code == "SAT006" && d.Message.Contains("1 miter"));
        var steep = new Vec2(Math.Cos(2.2), Math.Sin(2.2)) * 20; // 126° turn
        var lap = new SatinGenerator().Generate(Stroke([new(0, 0), new(20, 0), new Vec2(20, 0) + steep], 4), GenerationContext.Default);
        Assert.Contains(lap.Diagnostics, d => d.Code == "SAT006" && d.Message.Contains("1 lap"));
    }

    [Fact]
    public void Twisting_rails_are_reported()
    {
        var twisted = new SatinObject
        {
            Id = Guid.NewGuid(), Name = "t", ThreadIndex = 0, Source = SatinSource.Rails, Parameters = NoUnderlay,
            RailA = [new(0, 0), new(20, 0)],
            RailB = [new(0, 4), new(15, 4), new(5, 4.2), new(20, 4)],
        };
        Assert.Contains(SatinLadder.Build(twisted).Diagnostics, d => d.Code == "SAT007");
        Assert.DoesNotContain(SatinLadder.Build(Rails()).Diagnostics, d => d.Code == "SAT007");
    }

    [Fact]
    public void Tight_smooth_curve_is_not_mistaken_for_a_corner()
    {
        var result = new SatinGenerator().Generate(Stroke(Arc(3, 0, 300), 3), GenerationContext.Default);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "SAT006");
    }

    [Fact]
    public void Split_column_sewn_backwards_still_covers_both_legs()
    {
        var corner = Stroke([new(0, 0), new(20, 0), new(20, 20)], 4, NoUnderlay);
        var stitches = new SatinGenerator().Generate(corner, new GenerationContext(1, new Vec2(20, 20))).Value.Stitches;
        Assert.True(stitches[0].Position.Y > 18);
        Assert.True(stitches[^1].Position.X < 2);
    }

    private static SatinObject Rails(params Rung[] rungs) => new()
    {
        Id = Guid.NewGuid(), Name = "r", ThreadIndex = 0, Source = SatinSource.Rails,
        RailA = [new(0, 0), new(20, 0)], RailB = [new(0, 4), new(20, 4)], Rungs = rungs, Parameters = NoUnderlay,
    };

    [Fact]
    public void Rung_pins_facing_points_on_the_rails()
    {
        // A slanted rung: rail A at x=5 faces rail B at x=10.
        var ladder = SatinLadder.Build(Rails(new Rung(new(5, 0), new(10, 4)))).Value!;
        var index = Array.FindIndex(ladder.A, a => Math.Abs(a.X - 5) < 1e-6);
        Assert.True(index > 0);
        Assert.Equal(10, ladder.B[index].X, 6);
    }

    [Fact]
    public void Rungs_may_be_drawn_in_either_direction_and_crossing_rungs_are_ignored()
    {
        var reversed = SatinLadder.Build(Rails(new Rung(new(10, 4), new(5, 0)))).Value!;
        Assert.Equal(10, reversed.B[Array.FindIndex(reversed.A, a => Math.Abs(a.X - 5) < 1e-6)].X, 6);

        var crossing = SatinLadder.Build(Rails(new Rung(new(5, 0), new(15, 4)), new Rung(new(10, 0), new(8, 4))));
        Assert.Contains(crossing.Diagnostics, d => d.Code == "SAT005");
    }

    [Fact]
    public void Svg_convention_creates_rails_with_rungs_stroke_satin_and_type_overrides()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkstitch="http://inkstitch.org/namespace"
                 width="100mm" height="100mm" viewBox="0 0 100 100">
              <path id="leaf" inkstitch:satin_column="True" fill="none" stroke="#d4a017"
                    d="M0,0 L20,0 M0,4 L20,4 M5,-1 L10,5"/>
              <path id="scroll" d="M0,20 C10,10 20,30 30,20" fill="none" stroke="#d4a017" stroke-width="4" data-taper="3"/>
              <rect id="cut" x="0" y="40" width="50" height="30" fill="#d4a017" data-stitch="run"/>
              <path id="odd" d="M0,90 L10,90" stroke="#000" data-stitch="zigzag"/>
            </svg>
            """);
        var (_, objects, diagnostics) = ObjectFactory.FromArtwork(art);

        var leaf = Assert.IsType<SatinObject>(objects.Single(o => o.Name == "leaf"));
        Assert.Equal(SatinSource.Rails, leaf.Source);
        Assert.Single(leaf.Rungs);

        var scroll = Assert.IsType<SatinObject>(objects.Single(o => o.Name == "scroll"));
        Assert.Equal(SatinSource.Stroke, scroll.Source);
        Assert.Equal(4, scroll.WidthMm, 6);
        Assert.Equal(3, scroll.StartTaperMm, 6);

        Assert.IsType<RunObject>(objects.Single(o => o.Name == "cut"));
        Assert.Contains(diagnostics, d => d.Code == "IMP003");
    }

    [Fact]
    public void Pointed_leaf_tips_do_not_produce_duplicate_penetrations()
    {
        var leaf = new SatinObject
        {
            Id = Guid.NewGuid(), Name = "leaf", ThreadIndex = 0, Source = SatinSource.Rails, Parameters = NoUnderlay,
            RailA = [new(0, 0), new(10, -3), new(20, 0)], RailB = [new(0, 0), new(10, 3), new(20, 0)],
        };
        var pts = Top(leaf);
        Assert.DoesNotContain(pts.Zip(pts.Skip(1)), pair => pair.First.ApproximatelyEquals(pair.Second, 1e-6));
    }

    [Fact]
    public void Rails_satin_round_trips_through_embx_json()
    {
        var item = Rails(new Rung(new(5, 0), new(10, 4)));
        var json = System.Text.Json.JsonSerializer.Serialize<EmbroideryObject>(item, Embroidery.Application.Serialization.EmbroideryJson.Options);
        var back = Assert.IsType<SatinObject>(System.Text.Json.JsonSerializer.Deserialize<EmbroideryObject>(json, Embroidery.Application.Serialization.EmbroideryJson.Options));
        Assert.Equal(item.Rungs, back.Rungs);
        Assert.Equal(SatinSource.Rails, back.Source);
    }
}
