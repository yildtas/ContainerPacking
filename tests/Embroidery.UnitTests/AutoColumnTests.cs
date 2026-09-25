using Embroidery.Application.Import;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.UnitTests;

public class AutoColumnTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static Region Poly(params (double X, double Y)[] pts) => new([pts.Select(p => new Vec2(p.X, p.Y)).ToArray()]);

    private static readonly SatinObject Template = new() { Id = Guid.NewGuid(), Name = "harf", ThreadIndex = 0 };

    private AutoColumnProposal Propose(Region region)
    {
        var p = AutoColumns.Propose(region, Template);
        output.WriteLine($"{p.Columns.Count} columns, IoU {p.Coverage:0.000}, widest {p.MaxWidthMm:0.00}, reason {p.Reason}");
        return p;
    }

    [Fact]
    public void Long_bar_becomes_one_column()
    {
        var p = Propose(Poly((0, 0), (40, 0), (40, 4), (0, 4)));
        Assert.True(p.Accepted, p.Reason);
        var column = Assert.Single(p.Columns);
        Assert.Equal(Template.Id, column.Id);
        Assert.True(p.Coverage > 0.9);
        var ladder = SatinLadder.Build(column).Value!;
        Assert.InRange(ladder.MaxWidth, 3.8, 4.3);
        Assert.True(ladder.Length > 37, $"length {ladder.Length}");
    }

    [Fact]
    public void T_shape_becomes_several_columns()
    {
        var p = Propose(Poly((0, 0), (40, 0), (40, 4), (22, 4), (22, 30), (18, 30), (18, 4), (0, 4)));
        Assert.True(p.Accepted, p.Reason);
        Assert.Equal(3, p.Columns.Count);
        Assert.Equal(3, p.Columns.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Tapered_leaf_is_one_column()
    {
        var top = Enumerable.Range(0, 41).Select(i => (X: i * 1.0, Y: -3 * Math.Sin(Math.PI * i / 40)));
        var bottom = Enumerable.Range(0, 41).Reverse().Select(i => (X: i * 1.0, Y: 3 * Math.Sin(Math.PI * i / 40)));
        var p = Propose(Poly(top.Concat(bottom.Skip(1).SkipLast(1)).ToArray()));
        Assert.True(p.Accepted, p.Reason);
        Assert.Single(p.Columns);
    }

    [Fact]
    public void Curved_band_follows_the_curve()
    {
        var outer = Enumerable.Range(0, 37).Select(i => (X: 20 * Math.Cos(Math.PI * i / 36), Y: -20 * Math.Sin(Math.PI * i / 36)));
        var inner = Enumerable.Range(0, 37).Reverse().Select(i => (X: 16 * Math.Cos(Math.PI * i / 36), Y: -16 * Math.Sin(Math.PI * i / 36)));
        var p = Propose(Poly(outer.Concat(inner).ToArray()));
        Assert.True(p.Accepted, p.Reason);
        Assert.Single(p.Columns);
        Assert.True(p.Coverage > 0.9);
    }

    [Fact]
    public void Ring_becomes_a_closed_column()
    {
        var outer = Enumerable.Range(0, 72).Select(i => new Vec2(20 * Math.Cos(i * Math.PI / 36), 20 * Math.Sin(i * Math.PI / 36))).ToArray();
        var inner = outer.Select(v => v * 0.8).Reverse().ToArray();
        var p = Propose(new Region([outer, inner]));
        Assert.True(p.Accepted, p.Reason);
        Assert.Single(p.Columns);
    }

    [Fact]
    public void Wide_shapes_are_rejected()
    {
        Assert.False(Propose(Poly((0, 0), (30, 0), (30, 20), (0, 20))).Accepted);
        var disc = Enumerable.Range(0, 60).Select(i => (X: 5 * Math.Cos(i * Math.PI / 30), Y: 5 * Math.Sin(i * Math.PI / 30))).ToArray();
        Assert.False(Propose(Poly(disc)).Accepted);
    }
}

public class AutoColumnImportTests
{
    private const string Letters = """
        <svg xmlns="http://www.w3.org/2000/svg" width="80mm" height="40mm" viewBox="0 0 80 40">
          <path id="L" d="M5,5 L9,5 L9,31 L25,31 L25,35 L5,35 Z" fill="#c00"/>
          <rect id="blok" x="40" y="5" width="30" height="30" fill="#00c"/>
        </svg>
        """;

    [Fact]
    public void Narrow_filled_letters_import_as_satin_columns_and_sew_cleanly()
    {
        var service = new Embroidery.Application.Projects.ProjectService();
        var result = service.ImportSvg("harf.svg", Letters);
        var design = result.Design;
        Assert.Contains(result.Diagnostics, d => d.Code == "IMP005");
        Assert.All(design.Objects.Where(o => o.Name.StartsWith('L')), o => Assert.IsType<SatinObject>(o));
        Assert.True(design.Objects.Count(o => o.Name.StartsWith('L')) >= 2);
        Assert.IsType<TatamiObject>(Assert.Single(design.Objects, o => o.Name == "blok"));

        var plan = service.BuildPlan(design);
        Assert.True(!plan.Diagnostics.Any(d => d.Severity >= Embroidery.Core.Diagnostics.Severity.Warning), string.Join("; ", plan.Diagnostics.Select(d => d.Code + " " + d.Message)));
        Assert.DoesNotContain(plan.Diagnostics, d => d.Severity >= Embroidery.Core.Diagnostics.Severity.Warning && d.Code != "Q004");
    }

    [Fact]
    public void Converting_a_narrow_fill_to_satin_splices_in_its_columns()
    {
        var service = new Embroidery.Application.Projects.ProjectService();
        var design = service.ImportSvg("harf.svg", Letters.Replace("id=\"L\"", "id=\"L\" data-stitch=\"fill\"")).Design;
        var l = design.Objects.Single(o => o.Name == "L");
        Assert.Equal(StitchType.Tatami, l.StitchType);
        var before = design.Objects.Count;
        design = service.ConvertObject(design.Id, design.Revision, l.Id, StitchType.Satin);
        Assert.Equal(StitchType.Satin, design.FindObject(l.Id)!.StitchType);
        Assert.True(design.Objects.Count > before);
    }
}
