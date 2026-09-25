using Embroidery.Application.Serialization;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.StitchEngine;

namespace Embroidery.Application.Caching;

/// <summary>
/// Identifies one generated block. Content hash covers geometry + parameters; the resolved
/// entry is included because an object's stitches depend on where sewing starts.
/// </summary>
public sealed record ObjectGenerationKey(
    Guid ObjectId,
    string ContentHash,
    int EntryCandidate,
    Vec2 EntryPosition,
    string GeneratorVersion)
{
    public static ObjectGenerationKey For(EmbroideryObject item, GenerationContext context) => new(
        item.Id,
        // Name and visibility do not affect stitches.
        EmbroideryJson.Hash(item with { Name = "", Visible = true }),
        context.EntryCandidate,
        context.EntryPosition,
        StitchEngineInfo.GeneratorVersion);
}

/// <summary>Thread-safe LRU cache of generated blocks.</summary>
public sealed class GenerationCache(int capacity = 4096)
{
    private readonly object _gate = new();
    private readonly Dictionary<ObjectGenerationKey, LinkedListNode<(ObjectGenerationKey Key, GenerationResult<LogicalStitchBlock> Value)>> _map = [];
    private readonly LinkedList<(ObjectGenerationKey Key, GenerationResult<LogicalStitchBlock> Value)> _order = new();

    public int Hits { get; private set; }
    public int Misses { get; private set; }

    public GenerationResult<LogicalStitchBlock> GetOrAdd(ObjectGenerationKey key, Func<GenerationResult<LogicalStitchBlock>> factory)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                Hits++;
                return node.Value.Value;
            }
        }

        // Generate outside the lock; a concurrent duplicate generation is harmless (deterministic).
        var value = factory();
        lock (_gate)
        {
            Misses++;
            if (!_map.ContainsKey(key))
            {
                _map[key] = _order.AddFirst((key, value));
                while (_map.Count > capacity)
                {
                    var last = _order.Last!;
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
        }

        return value;
    }
}
