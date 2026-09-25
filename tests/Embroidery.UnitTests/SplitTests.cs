using Embroidery.Application.Projects;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.StitchEngine;

namespace Embroidery.UnitTests;

public class SplitTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void Run_splits_at_the_nearest_point()
    {
        var run = new RunObject { Id = Id, Name = "r", ThreadIndex = 0, Path = [new(0, 0), new(10, 0), new(10, 10)] };
        var (a, b) = ObjectEditing.Split(run, new Vec2(4, 1));
        Assert.Equal(Id, a.Id);
        Assert.NotEqual(Id, b.Id);
        Assert.Equal([new Vec2(0, 0), new Vec2(4, 0)], ((RunObject)a).Path);
        Assert.Equal([new Vec2(4, 0), new Vec2(10, 0), new Vec2(10, 10)], ((RunObject)b).Path);
    }

    [Fact]
    public void Rails_satin_halves_share_the_cut_as_a_rung_and_keep_their_rungs()
    {
        var satin = new SatinObject
        {
            Id = Id, Name = "s", ThreadIndex = 0, Source = SatinSource.Rails,
            RailA = [new(0, 0), new(30, 0)], RailB = [new(0, 4), new(30, 4)],
            Rungs = [new Rung(new(5, 0), new(5, 4)), new Rung(new(25, 4), new(25, 0))],
        };
        var (a, b) = ObjectEditing.Split(satin, new Vec2(15, 2));
        var first = (SatinObject)a;
        var second = (SatinObject)b;
        Assert.Equal(new Vec2(15, 0), first.RailA[^1]);
        Assert.Equal(new Vec2(15, 4), second.RailB[0]);
        Assert.Equal(2, first.Rungs.Count);
        Assert.Equal(2, second.Rungs.Count);
        Assert.Contains(first.Rungs, r => r.A == new Vec2(5, 0));
        Assert.Contains(second.Rungs, r => r.A == new Vec2(25, 4));
        foreach (var half in new[] { first, second })
        {
            var result = new ObjectGenerator().Generate(half, GenerationContext.Default);
            Assert.False(result.HasErrors);
        }
    }

    [Fact]
    public void Stroke_satin_keeps_outer_tapers_only()
    {
        var satin = new SatinObject
        {
            Id = Id, Name = "s", ThreadIndex = 0, Source = SatinSource.Stroke,
            Centerline = [new(0, 0), new(20, 0)], WidthMm = 3, StartTaperMm = 2, EndTaperMm = 2,
        };
        var (a, b) = ObjectEditing.Split(satin, new Vec2(10, 0));
        Assert.Equal((2.0, 0.0), (((SatinObject)a).StartTaperMm, ((SatinObject)a).EndTaperMm));
        Assert.Equal((0.0, 2.0), (((SatinObject)b).StartTaperMm, ((SatinObject)b).EndTaperMm));
    }

    [Fact]
    public void Fill_splits_across_its_rows()
    {
        var fill = new TatamiObject
        {
            Id = Id, Name = "t", ThreadIndex = 0,
            Region = new Region([new Vec2[] { new(0, 0), new(40, 0), new(40, 10), new(0, 10) }]),
            Parameters = new TatamiParameters { AngleDeg = 0 },
        };
        var (a, b) = ObjectEditing.Split(fill, new Vec2(10, 5));
        Assert.Equal(100, PolygonOps.Area(((TatamiObject)a).Region), 3);
        Assert.Equal(300, PolygonOps.Area(((TatamiObject)b).Region), 3);
        Assert.Throws<DesignValidationException>(() => ObjectEditing.Split(fill, new Vec2(100, 5)));
    }

    [Fact]
    public void Service_inserts_the_second_half_after_the_first_and_undo_restores()
    {
        var service = new ProjectService();
        var design = service.ImportSvg("s.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="50mm" height="20mm" viewBox="0 0 50 20">
              <path id="a" d="M5,5 L45,5" stroke="#000" stroke-width="0.3" fill="none"/>
              <path id="b" d="M5,15 L45,15" stroke="#000" stroke-width="0.3" fill="none"/>
            </svg>
            """).Design;
        var split = service.SplitObject(design.Id, design.Revision, design.Objects[0].Id, new Vec2(25, 5));
        Assert.Equal(["a", "a (2)", "b"], split.Objects.Select(o => o.Name));
        Assert.Throws<DesignValidationException>(() => service.SplitObject(design.Id, null, design.Objects[0].Id, new Vec2(5.1, 5)));
        Assert.Equal(2, service.Undo(design.Id).Objects.Count);
    }
}
