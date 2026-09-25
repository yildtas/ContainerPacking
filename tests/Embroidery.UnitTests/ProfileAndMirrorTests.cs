using Embroidery.Application.Analysis;
using Embroidery.Application.Projects;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;

namespace Embroidery.UnitTests;

public class ProfileAndMirrorTests
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="80mm" viewBox="0 0 100 80">
          <path id="kivrim" d="M10,40 C20,10 40,10 50,40 S80,70 90,40" stroke="#d4a53c" stroke-width="4" fill="none"/>
          <rect id="dolgu" x="10" y="55" width="30" height="15" fill="#d4a53c"/>
          <path id="bordur" d="M5,75 L95,75" stroke="#d4a53c" stroke-width="4" fill="none" data-stitch="rope"/>
          <path id="kesim" d="M2,2 L98,2" stroke="#d4a53c" stroke-width="0.3" fill="none" data-stitch="run"/>
        </svg>
        """;

    [Fact]
    public void Glossy_satin_profile_reproduces_the_fer7_density()
    {
        var service = new ProjectService();
        var design = service.ImportSvg("t.svg", Svg, stitchProfileId: StitchProfile.GlossySatin.Id).Design;
        Assert.Equal("glossy-satin", design.StitchProfileId);

        var satinOnly = design with { Objects = design.Objects.Where(o => o.Name == "kivrim").ToList() };
        var metrics = StitchMetrics.From(service.Encode(satinOnly).Encoded);
        // FER-7 reference: same-rail spacing p50 = 0.30 mm.
        Assert.InRange(metrics.SatinSameRailSpacingMm!.P50, 0.28, 0.32);
    }

    [Fact]
    public void Applying_a_profile_updates_every_object_and_can_be_undone()
    {
        var service = new ProjectService();
        var design = service.ImportSvg("t.svg", Svg).Design;
        var applied = service.ApplyStitchProfile(design.Id, design.Revision, StitchProfile.Metallic.Id);

        Assert.Equal(0.45, applied.Objects.OfType<SatinObject>().Single().Parameters.SpacingMm);
        Assert.Equal(0.45, applied.Objects.OfType<TatamiObject>().Single().Parameters.RowSpacingMm);
        Assert.Equal(3.0, applied.Objects.OfType<RunObject>().Single().Parameters.StitchLengthMm);
        Assert.Equal(0.40, applied.Objects.OfType<RopeObject>().Single().Parameters.SpacingMm);

        var undone = service.Undo(design.Id);
        Assert.Equal(0.40, undone.Objects.OfType<SatinObject>().Single().Parameters.SpacingMm);
        Assert.Equal("standard", undone.StitchProfileId);

        Assert.Throws<DesignValidationException>(() => service.ApplyStitchProfile(design.Id, null, "nope"));
    }

    [Fact]
    public void Mirror_flips_geometry_fill_angle_and_rope_twist()
    {
        var service = new ProjectService();
        var design = service.ImportSvg("t.svg", Svg).Design;
        var mirrored = service.Mirror(design.Id, design.Revision, MirrorAxis.Horizontal);

        Bounds All(Design d) => d.Objects.Aggregate(Bounds.Empty, (b, o) => b.Include(o.Bounds));
        var before = All(design);
        var after = All(mirrored);
        Assert.Equal(before.MinX, after.MinX, 6);
        Assert.Equal(before.MaxX, after.MaxX, 6);

        var satinBefore = design.Objects.OfType<SatinObject>().Single().Centerline;
        var satinAfter = mirrored.Objects.OfType<SatinObject>().Single().Centerline;
        Assert.Equal(before.MinX + before.MaxX - satinBefore[0].X, satinAfter[0].X, 6);
        Assert.Equal(satinBefore[0].Y, satinAfter[0].Y, 6);

        Assert.Equal(-45, mirrored.Objects.OfType<TatamiObject>().Single().Parameters.AngleDeg, 6);
        Assert.Equal(TwistDirection.Z, mirrored.Objects.OfType<RopeObject>().Single().Parameters.Twist);

        // Same stitch count either way: a mirror image, not a different digitizing.
        var a = StitchMetrics.From(service.Encode(design).Encoded);
        var b = StitchMetrics.From(service.Encode(mirrored).Encoded);
        Assert.InRange(b.Stitches, a.Stitches * 0.97, a.Stitches * 1.03);

        var twice = DesignTransforms.Mirror(mirrored, MirrorAxis.Horizontal);
        Assert.Equal(design.Objects.OfType<SatinObject>().Single().Centerline[5].X, twice.Objects.OfType<SatinObject>().Single().Centerline[5].X, 6);
    }

    [Fact]
    public void Import_picks_the_smallest_hoop_that_fits()
    {
        var service = new ProjectService();
        // Objects span about 90 × 75 mm.
        Assert.Equal("100x100", service.ImportSvg("t.svg", Svg).Design.Hoop.Name);
        // Scaled 1.5× (≈135 × 113 mm): fits the 130 × 180 hoop turned by 90°.
        Assert.Equal("130x180", service.ImportSvg("t.svg", Svg.Replace("width=\"100mm\" height=\"80mm\"", "width=\"150mm\" height=\"120mm\"")).Design.Hoop.Name);

        var big = """<svg xmlns="http://www.w3.org/2000/svg" width="290mm" height="505mm" viewBox="0 0 290 505"><rect width="288" height="504" fill="#000"/></svg>""";
        var hoop = service.ImportSvg("big.svg", big).Design.Hoop;
        Assert.True(hoop.WidthMm >= 288 && hoop.HeightMm >= 504, hoop.Name);
    }
}
