using Embroidery.Core.Objects;

namespace Embroidery.Core.Model;

/// <summary>The original artwork a design was digitized from.</summary>
public sealed record SourceArtwork(string FileName, string SvgText, double ScaleMmPerUnit);

/// <summary>
/// The editable design: the single source of truth. Stitch plans are derived from it
/// and can always be regenerated. Instances are immutable; every edit produces a new
/// design with a higher <see cref="Revision"/>.
/// </summary>
public sealed record Design
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public long Revision { get; init; }
    public SourceArtwork? Artwork { get; init; }
    public IReadOnlyList<EmbroideryThread> Threads { get; init; } = [];

    /// <summary>Objects in sew order.</summary>
    public IReadOnlyList<EmbroideryObject> Objects { get; init; } = [];

    public Hoop Hoop { get; init; } = Hoop.Default;
    public string MachineProfileId { get; init; } = "generic-dst";
    public ConnectionPolicy Connections { get; init; } = ConnectionPolicy.Default;

    public EmbroideryObject? FindObject(Guid id) => Objects.FirstOrDefault(o => o.Id == id);
}
