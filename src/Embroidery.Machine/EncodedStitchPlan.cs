namespace Embroidery.Machine;

public enum EncodedCommand : byte
{
    Stitch,
    Jump,
    ColorChange,
    End,
}

/// <summary>An absolute position in machine units (Y up, origin at design centre).</summary>
public readonly record struct EncodedStitch(int X, int Y, EncodedCommand Command);

/// <summary>
/// Output of the machine encoder: only commands the target format can express, every move
/// within the per-record limit. Format writers serialise this without making decisions.
/// </summary>
public sealed record EncodedStitchPlan(string Label, IReadOnlyList<EncodedStitch> Stitches)
{
    public int StitchCount => Stitches.Count(s => s.Command == EncodedCommand.Stitch);
    public int ColorChangeCount => Stitches.Count(s => s.Command == EncodedCommand.ColorChange);

    /// <summary>Extents relative to the origin, in machine units (all non-negative).</summary>
    public (int PlusX, int MinusX, int PlusY, int MinusY) Extents
    {
        get
        {
            int maxX = 0, minX = 0, maxY = 0, minY = 0;
            foreach (var s in Stitches)
            {
                maxX = Math.Max(maxX, s.X);
                minX = Math.Min(minX, s.X);
                maxY = Math.Max(maxY, s.Y);
                minY = Math.Min(minY, s.Y);
            }

            return (maxX, -minX, maxY, -minY);
        }
    }
}
