using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.StitchEngine;

public static class StitchEngineInfo
{
    /// <summary>
    /// Bump whenever any generator's output changes for the same input; cached blocks
    /// produced by another version are discarded.
    /// </summary>
    public const string GeneratorVersion = "1.0.0";
}

/// <summary>
/// Resolved inputs a generator needs besides the object itself. <see cref="EntryCandidate"/>
/// indexes <see cref="EntryCandidates.For"/>; <see cref="EntryPosition"/> is the exact start
/// point to aim for. Both are part of the cache key.
/// </summary>
public sealed record GenerationContext(int EntryCandidate, Vec2 EntryPosition)
{
    public static readonly GenerationContext Default = new(0, Vec2.Zero);
}

public interface IStitchGenerator<in TObject> where TObject : EmbroideryObject
{
    GenerationResult<LogicalStitchBlock> Generate(TObject item, GenerationContext context, CancellationToken ct = default);
}

/// <summary>Dispatches an object to its generator.</summary>
public sealed class ObjectGenerator
{
    private readonly RunGenerator _run = new();
    private readonly SatinGenerator _satin = new();
    private readonly TatamiGenerator _tatami = new();

    public GenerationResult<LogicalStitchBlock> Generate(EmbroideryObject item, GenerationContext context, CancellationToken ct = default)
    {
        try
        {
            return item switch
            {
                RunObject r => _run.Generate(r, context, ct),
                SatinObject s => _satin.Generate(s, context, ct),
                TatamiObject t => _tatami.Generate(t, context, ct),
                _ => Failed(item, $"Unsupported object type {item.GetType().Name}."),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One broken object must not take the whole plan down.
            return Failed(item, $"Stitch generation failed: {ex.Message}");
        }
    }

    private static GenerationResult<LogicalStitchBlock> Failed(EmbroideryObject item, string message) =>
        new(new LogicalStitchBlock(BlockKind.Object, item.Id, item.ThreadIndex, []),
            [Diagnostic.Error("GEN001", message, item.Id)]);
}

/// <summary>
/// Small, fixed sets of possible start points per object. The sequencer picks one, which
/// keeps cache hits high when neighbouring objects change.
/// </summary>
public static class EntryCandidates
{
    public static IReadOnlyList<Vec2> For(EmbroideryObject item) => item switch
    {
        RunObject r when r.Path.Count > 0 => r.Path[0].ApproximatelyEquals(r.Path[^1], 1e-6)
            ? [r.Path[0]]
            : [r.Path[0], r.Path[^1]],
        SatinObject s when s.RailA.Count > 0 && s.RailB.Count > 0 =>
            [Vec2.Lerp(s.RailA[0], s.RailB[0], 0.5), Vec2.Lerp(s.RailA[^1], s.RailB[^1], 0.5)],
        TatamiObject t when !t.Bounds.IsEmpty =>
        [
            new Vec2(t.Bounds.MinX, t.Bounds.MinY), new Vec2(t.Bounds.MaxX, t.Bounds.MinY),
            new Vec2(t.Bounds.MaxX, t.Bounds.MaxY), new Vec2(t.Bounds.MinX, t.Bounds.MaxY),
        ],
        _ => [Vec2.Zero],
    };

    public static GenerationContext Resolve(EmbroideryObject item, Vec2? previousExit)
    {
        var candidates = For(item);
        var target = item.EntryPoint ?? previousExit;
        if (target is null) return new GenerationContext(0, candidates[0]);

        var best = 0;
        for (var i = 1; i < candidates.Count; i++)
        {
            if (Vec2.Distance(candidates[i], target.Value) < Vec2.Distance(candidates[best], target.Value)) best = i;
        }

        // A user-fixed entry point is honoured exactly; otherwise aim at the candidate itself.
        return new GenerationContext(best, item.EntryPoint ?? candidates[best]);
    }
}
