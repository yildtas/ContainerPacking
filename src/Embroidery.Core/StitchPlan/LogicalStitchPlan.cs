using Embroidery.Core.Diagnostics;
using Embroidery.Core.Primitives;

namespace Embroidery.Core.StitchPlan;

/// <summary>
/// Machine-independent commands. <see cref="Trim"/>, <see cref="TieIn"/> and <see cref="TieOff"/>
/// are intents: the machine encoder decides how to realise them for a given format.
/// </summary>
public enum StitchCommand : byte
{
    /// <summary>Needle penetration reached by a visible stitch.</summary>
    Stitch,

    /// <summary>Penetration reached by a hidden travel stitch (sewn, meant to be covered).</summary>
    Travel,

    /// <summary>Frame move without sewing.</summary>
    Jump,

    Trim,
    ColorChange,
    Stop,
    TieIn,
    TieOff,
    End,
}

public enum StitchLayer : byte
{
    Top,
    Underlay,
    Connector,
}

/// <summary>One command at an absolute position in millimetres.</summary>
public readonly record struct LogicalStitch(Vec2 Position, StitchCommand Command, StitchLayer Layer = StitchLayer.Top);

public enum BlockKind
{
    Object,
    Connector,
}

/// <summary>
/// The stitches of one object (or one connector between objects). Object id and thread
/// live here rather than on every stitch to keep large plans compact.
/// </summary>
public sealed record LogicalStitchBlock(
    BlockKind Kind,
    Guid ObjectId,
    int ThreadIndex,
    IReadOnlyList<LogicalStitch> Stitches)
{
    public Vec2? FirstPosition => Stitches.Count > 0 ? Stitches[0].Position : null;
    public Vec2? LastPosition => Stitches.Count > 0 ? Stitches[^1].Position : null;
}

public sealed record PlanStatistics(
    int StitchCount,
    int JumpCount,
    int TrimCount,
    int ColorChangeCount,
    Bounds Bounds,
    double ThreadLengthMm);

/// <summary>Format-independent output of the stitch engine.</summary>
public sealed record LogicalStitchPlan(
    IReadOnlyList<LogicalStitchBlock> Blocks,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public IEnumerable<LogicalStitch> AllStitches => Blocks.SelectMany(b => b.Stitches);

    public PlanStatistics ComputeStatistics()
    {
        int stitches = 0, jumps = 0, trims = 0, colors = 0;
        var bounds = Bounds.Empty;
        double thread = 0;
        Vec2? prev = null;
        foreach (var s in AllStitches)
        {
            switch (s.Command)
            {
                case StitchCommand.Stitch or StitchCommand.Travel:
                    stitches++;
                    bounds = bounds.Include(s.Position);
                    if (prev is { } p) thread += Vec2.Distance(p, s.Position);
                    break;
                case StitchCommand.Jump: jumps++; break;
                case StitchCommand.Trim: trims++; break;
                case StitchCommand.ColorChange: colors++; break;
            }

            if (s.Command is not (StitchCommand.Trim or StitchCommand.ColorChange or StitchCommand.Stop
                or StitchCommand.TieIn or StitchCommand.TieOff or StitchCommand.End))
            {
                prev = s.Position;
            }
        }

        return new PlanStatistics(stitches, jumps, trims, colors, bounds, thread);
    }
}
