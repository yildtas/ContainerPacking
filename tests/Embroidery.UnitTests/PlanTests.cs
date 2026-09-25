using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.StitchEngine;
using Embroidery.StitchEngine.Quality;
using Embroidery.StitchEngine.Sequencing;

namespace Embroidery.UnitTests;

public class PlanTests
{
    private static readonly ObjectGenerator Generator = new();

    private static RunObject Line(double x0, double y0, double x1, double y1, int thread = 0) => new()
    {
        Id = Guid.NewGuid(), Name = "line", ThreadIndex = thread, Path = [new(x0, y0), new(x1, y1)],
    };

    private static Design DesignOf(params EmbroideryObject[] objects) => new()
    {
        Id = Guid.NewGuid(), Name = "test",
        Threads = [new EmbroideryThread("A", "#FF0000"), new EmbroideryThread("B", "#0000FF")],
        Objects = objects,
    };

    private static LogicalStitchPlan Build(Design d) => PlanBuilder.Build(d, (o, c, ct) => Generator.Generate(o, c, ct));

    private static List<StitchCommand> ConnectorCommands(LogicalStitchPlan plan, int connectorIndex) =>
        plan.Blocks.Where(b => b.Kind == BlockKind.Connector).ElementAt(connectorIndex).Stitches.Select(s => s.Command).ToList();

    [Fact]
    public void Far_apart_objects_are_connected_with_tie_trim_jump_tie()
    {
        var plan = Build(DesignOf(Line(0, 0, 10, 0), Line(40, 0, 50, 0)));
        Assert.Equal([StitchCommand.Jump, StitchCommand.TieIn], ConnectorCommands(plan, 0));
        Assert.Equal([StitchCommand.TieOff, StitchCommand.Trim, StitchCommand.Jump, StitchCommand.TieIn], ConnectorCommands(plan, 1));
        Assert.Equal([StitchCommand.TieOff, StitchCommand.Trim, StitchCommand.End], ConnectorCommands(plan, 2));
    }

    [Fact]
    public void Close_objects_are_sewn_directly_and_medium_gaps_jump()
    {
        var direct = Build(DesignOf(Line(0, 0, 10, 0), Line(12, 0, 20, 0)));
        Assert.Equal(2, direct.Blocks.Count(b => b.Kind == BlockKind.Connector)); // start + end only

        var jump = Build(DesignOf(Line(0, 0, 10, 0), Line(15, 0, 20, 0)));
        Assert.Equal([StitchCommand.Jump], ConnectorCommands(jump, 1));
    }

    [Fact]
    public void Entry_point_follows_previous_exit()
    {
        // The second line is closer to the first line's end at its far end, so it is sewn backwards.
        var second = Line(30, 0, 12, 0);
        var plan = Build(DesignOf(Line(0, 0, 10, 0), second));
        var block = plan.Blocks.Single(b => b.Kind == BlockKind.Object && b.ObjectId == second.Id);
        Assert.Equal(new Vec2(12, 0), block.Stitches[0].Position);
    }

    [Fact]
    public void Thread_change_adds_color_change()
    {
        var plan = Build(DesignOf(Line(0, 0, 10, 0), Line(11, 0, 20, 0, thread: 1)));
        Assert.Contains(StitchCommand.ColorChange, ConnectorCommands(plan, 1));
        Assert.Equal(1, plan.ComputeStatistics().ColorChangeCount);
    }

    [Fact]
    public void Unknown_thread_and_hidden_objects_are_skipped()
    {
        var hidden = Line(0, 0, 10, 0) with { Visible = false };
        var bad = Line(0, 0, 10, 0, thread: 9);
        var plan = Build(DesignOf(hidden, bad));
        Assert.Empty(plan.Blocks);
        Assert.Contains(plan.Diagnostics, d => d.Code == "Q006");
    }

    [Fact]
    public void Quality_pass_flags_designs_larger_than_the_hoop()
    {
        var design = DesignOf(Line(0, 0, 200, 0)) with { Hoop = new Hoop("small", 100, 100) };
        var checkedPlan = QualityPass.Run(Build(design), design);
        Assert.Contains(checkedPlan.Diagnostics, d => d.Code == "Q004");
    }
}
