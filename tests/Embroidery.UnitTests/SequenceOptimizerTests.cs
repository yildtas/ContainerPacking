using Embroidery.Application.Projects;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.StitchEngine.Sequencing;

namespace Embroidery.UnitTests;

public class SequenceOptimizerTests
{
    private static RunObject Line(string name, double x, int thread = 0) => new()
    {
        Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, Path = [new(x, 0), new(x + 5, 0)],
    };

    private static TatamiObject Square(string name, double x, int thread) => new()
    {
        Id = Guid.NewGuid(), Name = name, ThreadIndex = thread,
        Region = new Region([new Vec2[] { new(x, -10), new(x + 20, -10), new(x + 20, 10), new(x, 10) }]),
    };

    private static Design Of(params EmbroideryObject[] objects) => new()
    {
        Id = Guid.NewGuid(), Name = "t",
        Threads = [new EmbroideryThread("a", "#FF0000"), new EmbroideryThread("b", "#0000FF")],
        Objects = objects,
    };

    private static List<string> Names(Design d, SequenceResult r) =>
        r.Order.Select(id => d.Objects.Single(o => o.Id == id).Name).ToList();

    [Fact]
    public void Separate_objects_are_grouped_by_thread()
    {
        var d = Of(Line("a1", 0, 0), Line("b1", 30, 1), Line("a2", 60, 0), Line("b2", 90, 1));
        var r = SequenceOptimizer.Optimize(d);
        Assert.True(r.Improved);
        Assert.Equal(3, r.Before.ColorChanges);
        Assert.Equal(1, r.After.ColorChanges);
        Assert.Equal(["a1", "a2", "b2", "b1"], Names(d, r));
    }

    [Fact]
    public void Overlapping_objects_keep_their_stacking_order()
    {
        // "top" (thread a) lies on "base" (thread b): it must stay after it even though
        // sewing it together with the other thread-a object would save a colour change.
        var baseFill = Square("base", 0, 1);
        var top = Line("top", 5, 0) with { Path = [new(2, 0), new(18, 0)] };
        var d = Of(Line("first", -40, 0), baseFill, top);
        var r = SequenceOptimizer.Optimize(d);
        var names = Names(d, r);
        Assert.True(names.IndexOf("base") < names.IndexOf("top"));
        Assert.Equal(1, r.Constraints);
    }

    [Fact]
    public void Zig_zag_order_is_shortened()
    {
        var d = Of(Line("1", 0), Line("2", 100), Line("3", 10), Line("4", 110), Line("5", 20));
        var r = SequenceOptimizer.Optimize(d);
        Assert.True(r.After.TravelMm < r.Before.TravelMm / 2, $"{r.Before.TravelMm} -> {r.After.TravelMm}");
        Assert.Equal("1", Names(d, r)[0]); // the designer's start is kept
    }

    [Fact]
    public void Service_applies_only_improvements_and_can_undo()
    {
        var service = new ProjectService();
        var design = service.ImportSvg("t.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="120mm" height="20mm" viewBox="0 0 120 20">
              <path d="M0,10 L5,10" stroke="#f00" stroke-width="0.3" fill="none"/>
              <path d="M30,10 L35,10" stroke="#00f" stroke-width="0.3" fill="none"/>
              <path d="M60,10 L65,10" stroke="#f00" stroke-width="0.3" fill="none"/>
            </svg>
            """).Design;
        var (optimized, result) = service.OptimizeOrder(design.Id, design.Revision);
        Assert.True(result.Improved);
        Assert.Equal(design.Revision + 1, optimized.Revision);

        var (again, second) = service.OptimizeOrder(optimized.Id, optimized.Revision);
        Assert.False(second.Improved);
        Assert.Equal(optimized.Revision, again.Revision);

        Assert.Equal(design.Objects.Select(o => o.Id), service.Undo(design.Id).Objects.Select(o => o.Id));
    }
}
