using System.Text.Json;
using Embroidery.Application.Import;
using Embroidery.Application.Serialization;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Geometry.Svg;
using Embroidery.StitchEngine;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.UnitTests;

public class RopeTests
{
    private static RopeObject Rope(TwistDirection twist = TwistDirection.S, IReadOnlyList<Vec2>? path = null) => new()
    {
        Id = Guid.NewGuid(), Name = "halat", ThreadIndex = 0, WidthMm = 4,
        Path = path ?? [new(0, 0), new(60, 0)],
        Parameters = new RopeParameters { PitchMm = 3, StrandLengthMm = 6, Twist = twist },
    };

    private static IReadOnlyList<LogicalStitch> Stitches(RopeObject r, int entry = 0) =>
        new RopeGenerator().Generate(r, new GenerationContext(entry, r.Path[entry == 0 ? 0 : ^1])).Value.Stitches;

    [Fact]
    public void Strands_stay_inside_the_band()
    {
        var top = Stitches(Rope()).Where(s => s.Layer == StitchLayer.Top).ToList();
        Assert.NotEmpty(top);
        Assert.All(top, s => Assert.InRange(s.Position.Y, -2.3, 2.3));
        Assert.All(top, s => Assert.InRange(s.Position.X, -0.5, 60.5));
    }

    [Fact]
    public void S_and_Z_twists_slant_opposite_ways()
    {
        static double Slant(IReadOnlyList<LogicalStitch> st)
        {
            // Sign of the throw direction relative to the band: average of dx·dy over long throws.
            var top = st.Where(s => s.Layer == StitchLayer.Top).Select(s => s.Position).ToList();
            return top.Zip(top.Skip(1), (a, b) => b - a).Where(d => d.Length > 1.5).Average(d => Math.Sign(d.X * d.Y));
        }

        Assert.True(Slant(Stitches(Rope(TwistDirection.S))) * Slant(Stitches(Rope(TwistDirection.Z))) < 0);
    }

    [Fact]
    public void Hops_between_strands_are_short()
    {
        var st = Stitches(Rope()).Select(s => s.Position).ToList();
        var longest = st.Zip(st.Skip(1), Vec2.Distance).Max();
        Assert.True(longest <= 7.0, $"longest stitch {longest:0.00} mm");
    }

    [Fact]
    public void Rope_starts_and_ends_at_the_chosen_end()
    {
        var forward = Stitches(Rope());
        Assert.True(Vec2.Distance(forward[0].Position, new Vec2(0, 0)) < 0.1);
        Assert.True(forward[^1].Position.X < 8);

        var backward = Stitches(Rope(), entry: 1);
        Assert.True(Vec2.Distance(backward[0].Position, new Vec2(60, 0)) < 0.1);
        Assert.True(backward[^1].Position.X > 52);
    }

    [Fact]
    public void Too_short_path_is_an_error()
    {
        var result = new RopeGenerator().Generate(Rope(path: [new(0, 0), new(3, 0)]), GenerationContext.Default);
        Assert.Contains(result.Diagnostics, d => d.Code == "ROPE001");
    }

    [Fact]
    public void Svg_rope_hint_and_json_round_trip()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="100mm" viewBox="0 0 100 100">
              <path id="bordur" d="M5,90 L95,90" stroke="#d4a53c" stroke-width="4.5" fill="none" data-stitch="rope" data-pitch="3.5"/>
            </svg>
            """);
        var rope = Assert.IsType<RopeObject>(Assert.Single(ObjectFactory.FromArtwork(art).Objects));
        Assert.Equal(4.5, rope.WidthMm, 6);
        Assert.Equal(3.5, rope.Parameters.PitchMm, 6);

        var json = JsonSerializer.Serialize<EmbroideryObject>(rope, EmbroideryJson.Options);
        var back = Assert.IsType<RopeObject>(JsonSerializer.Deserialize<EmbroideryObject>(json, EmbroideryJson.Options));
        Assert.Equal(rope.Parameters, back.Parameters);
        Assert.Equal(rope.Path, back.Path);
    }
}
