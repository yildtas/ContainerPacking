using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;

namespace Embroidery.StitchEngine.Sequencing;

/// <summary>Produces an object's block for a resolved context; the application layer wraps this with its cache.</summary>
public delegate GenerationResult<LogicalStitchBlock> BlockProvider(EmbroideryObject item, GenerationContext context, CancellationToken ct);

/// <summary>
/// Sequences objects in design order, resolves entry points, and inserts connectors
/// (direct stitch, jump, or tie-off + trim + jump + tie-in) according to the
/// <see cref="ConnectionPolicy"/>. Tie and trim are intents for the machine encoder.
/// Codes: Q005 empty block, Q006 unknown thread.
/// </summary>
public static class PlanBuilder
{
    public static LogicalStitchPlan Build(Design design, BlockProvider provider, ConnectionPolicy? policyOverride = null, CancellationToken ct = default)
    {
        var policy = policyOverride ?? design.Connections;
        var blocks = new List<LogicalStitchBlock>();
        var diagnostics = new List<Diagnostic>();
        Vec2? previousExit = null;
        int? previousThread = null;

        foreach (var item in design.Objects)
        {
            ct.ThrowIfCancellationRequested();
            if (!item.Visible) continue;
            if (item.ThreadIndex < 0 || item.ThreadIndex >= design.Threads.Count)
            {
                diagnostics.Add(Diagnostic.Error("Q006", $"Object '{item.Name}' refers to thread {item.ThreadIndex}, which does not exist.", item.Id));
                continue;
            }

            var context = EntryCandidates.Resolve(item, previousExit);
            var result = provider(item, context, ct);
            diagnostics.AddRange(result.Diagnostics);
            var block = result.Value;
            if (block.Stitches.Count == 0)
            {
                if (!result.HasErrors) diagnostics.Add(Diagnostic.Warning("Q005", $"Object '{item.Name}' produced no stitches.", item.Id));
                continue;
            }

            var first = block.Stitches[0].Position;
            var connector = new List<LogicalStitch>();
            if (previousExit is not { } exit)
            {
                connector.Add(new(first, StitchCommand.Jump, StitchLayer.Connector));
                if (policy.TieStitches) connector.Add(new(first, StitchCommand.TieIn, StitchLayer.Connector));
            }
            else if (previousThread != item.ThreadIndex)
            {
                if (policy.TieStitches) connector.Add(new(exit, StitchCommand.TieOff, StitchLayer.Connector));
                if (policy.TrimOnColorChange) connector.Add(new(exit, StitchCommand.Trim, StitchLayer.Connector));
                connector.Add(new(exit, StitchCommand.ColorChange, StitchLayer.Connector));
                connector.Add(new(first, StitchCommand.Jump, StitchLayer.Connector));
                if (policy.TieStitches) connector.Add(new(first, StitchCommand.TieIn, StitchLayer.Connector));
            }
            else
            {
                var gap = Vec2.Distance(exit, first);
                if (gap > policy.TrimAboveMm)
                {
                    if (policy.TieStitches) connector.Add(new(exit, StitchCommand.TieOff, StitchLayer.Connector));
                    connector.Add(new(exit, StitchCommand.Trim, StitchLayer.Connector));
                    connector.Add(new(first, StitchCommand.Jump, StitchLayer.Connector));
                    if (policy.TieStitches) connector.Add(new(first, StitchCommand.TieIn, StitchLayer.Connector));
                }
                else if (gap > policy.MaxDirectStitchMm)
                {
                    connector.Add(new(first, StitchCommand.Jump, StitchLayer.Connector));
                }

                // Shorter gaps are sewn directly by the block's first stitch.
            }

            if (connector.Count > 0) blocks.Add(new LogicalStitchBlock(BlockKind.Connector, item.Id, item.ThreadIndex, connector));
            blocks.Add(block);
            previousExit = block.Stitches[^1].Position;
            previousThread = item.ThreadIndex;
        }

        if (previousExit is { } end)
        {
            var tail = new List<LogicalStitch>();
            if (policy.TieStitches) tail.Add(new(end, StitchCommand.TieOff, StitchLayer.Connector));
            tail.Add(new(end, StitchCommand.Trim, StitchLayer.Connector));
            tail.Add(new(end, StitchCommand.End, StitchLayer.Connector));
            blocks.Add(new LogicalStitchBlock(BlockKind.Connector, Guid.Empty, previousThread ?? 0, tail));
        }

        return new LogicalStitchPlan(blocks, diagnostics);
    }
}
