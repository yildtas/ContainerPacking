using System.Text.Json.Serialization;
using Embroidery.Core.Primitives;

namespace Embroidery.Core.Objects;

public enum StitchType
{
    Run,
    Satin,
    Tatami,
    Rope,
}

/// <summary>
/// An editable embroidery object: geometry plus stitch parameters. This is source data;
/// the stitches it produces are derived and cached.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RunObject), "run")]
[JsonDerivedType(typeof(SatinObject), "satin")]
[JsonDerivedType(typeof(TatamiObject), "tatami")]
[JsonDerivedType(typeof(RopeObject), "rope")]
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

/// <summary>Which geometry is authoritative for a satin column; the other is derived.</summary>
public enum SatinSource
{
    /// <summary>Two rails (+ optional rungs) drawn by the digitizer. For variable-width parts such as leaves.</summary>
    Rails,

    /// <summary>A centre line with a width and optional tapered ends. For constant-width scrolls.</summary>
    Stroke,
}

/// <summary>A user-drawn line across the column fixing which rail points face each other.</summary>
public readonly record struct Rung(Vec2 A, Vec2 B);

/// <summary>
/// A satin column. Exactly one geometry is the source of truth, chosen by <see cref="Source"/>:
/// rails + rungs, or centre line + width. Editing one never silently rewrites the other.
/// </summary>
public sealed record SatinObject : EmbroideryObject
{
    public SatinSource Source { get; init; } = SatinSource.Rails;

    // Rails source: both rails run in the same direction. Without rungs, points are
    // matched by normalised arc length; each rung pins a pair of facing points.
    public IReadOnlyList<Vec2> RailA { get; init; } = [];
    public IReadOnlyList<Vec2> RailB { get; init; } = [];
    public IReadOnlyList<Rung> Rungs { get; init; } = [];

    // Stroke source.
    public IReadOnlyList<Vec2> Centerline { get; init; } = [];
    public double WidthMm { get; init; } = 4.0;

    /// <summary>Length over which the column widens from a point to full width at its start.</summary>
    public double StartTaperMm { get; init; }

    public double EndTaperMm { get; init; }

    public SatinParameters Parameters { get; init; } = new();

    public override StitchType StitchType => StitchType.Satin;

    public override Bounds Bounds
    {
        get
        {
            if (Source == SatinSource.Rails) return Bounds.Of(RailA).Include(Bounds.Of(RailB));
            var b = Bounds.Of(Centerline);
            if (b.IsEmpty) return b;
            var h = WidthMm / 2;
            return new Bounds(b.MinX - h, b.MinY - h, b.MaxX + h, b.MaxY + h);
        }
    }
}

public sealed record TatamiObject : EmbroideryObject
{
    public required Region Region { get; init; }
    public TatamiParameters Parameters { get; init; } = new();

    public override StitchType StitchType => StitchType.Tatami;
    public override Bounds Bounds => Region.Bounds;
}

/// <summary>
/// A twisted-cord border ("halat"): a band of <see cref="WidthMm"/> along <see cref="Path"/>
/// filled with slanted satin strands, like the borders of the FER-7 reference.
/// </summary>
public sealed record RopeObject : EmbroideryObject
{
    public required IReadOnlyList<Vec2> Path { get; init; }
    public double WidthMm { get; init; } = 4.0;
    public RopeParameters Parameters { get; init; } = new();

    public override StitchType StitchType => StitchType.Rope;

    public override Bounds Bounds
    {
        get
        {
            var b = Bounds.Of(Path);
            if (b.IsEmpty) return b;
            var h = WidthMm / 2;
            return new Bounds(b.MinX - h, b.MinY - h, b.MaxX + h, b.MaxY + h);
        }
    }
}
