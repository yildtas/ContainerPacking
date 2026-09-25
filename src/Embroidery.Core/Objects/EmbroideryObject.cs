using System.Text.Json.Serialization;
using Embroidery.Core.Primitives;

namespace Embroidery.Core.Objects;

public enum StitchType
{
    Run,
    Satin,
    Tatami,
}

/// <summary>
/// An editable embroidery object: geometry plus stitch parameters. This is source data;
/// the stitches it produces are derived and cached.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RunObject), "run")]
[JsonDerivedType(typeof(SatinObject), "satin")]
[JsonDerivedType(typeof(TatamiObject), "tatami")]
public abstract record EmbroideryObject
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int ThreadIndex { get; init; }

    /// <summary>Hidden objects stay in the design but are not stitched.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Optional user-fixed start point. When null the sequencer chooses one.</summary>
    public Vec2? EntryPoint { get; init; }

    [JsonIgnore]
    public abstract StitchType StitchType { get; }

    [JsonIgnore]
    public abstract Bounds Bounds { get; }
}

public sealed record RunObject : EmbroideryObject
{
    /// <summary>Path to follow. For a closed outline the first point is repeated at the end.</summary>
    public required IReadOnlyList<Vec2> Path { get; init; }

    public RunParameters Parameters { get; init; } = new();

    public override StitchType StitchType => StitchType.Run;
    public override Bounds Bounds => Bounds.Of(Path);
}

/// <summary>
/// A satin column between two rails that run in the same direction. Rungs are
/// implied by matching the rails by normalised arc length.
/// </summary>
public sealed record SatinObject : EmbroideryObject
{
    public required IReadOnlyList<Vec2> RailA { get; init; }
    public required IReadOnlyList<Vec2> RailB { get; init; }
    public SatinParameters Parameters { get; init; } = new();

    public override StitchType StitchType => StitchType.Satin;
    public override Bounds Bounds => Bounds.Of(RailA).Include(Bounds.Of(RailB));
}

public sealed record TatamiObject : EmbroideryObject
{
    public required Region Region { get; init; }
    public TatamiParameters Parameters { get; init; } = new();

    public override StitchType StitchType => StitchType.Tatami;
    public override Bounds Bounds => Region.Bounds;
}
