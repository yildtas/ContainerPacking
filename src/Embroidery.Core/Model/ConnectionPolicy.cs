namespace Embroidery.Core.Model;

/// <summary>
/// Decides how two consecutive stitch blocks are connected. Lives in the sequencing
/// stage (machine independent); a machine profile may supply different values.
/// </summary>
public sealed record ConnectionPolicy
{
    /// <summary>Gaps up to this distance are sewn directly as a normal stitch.</summary>
    public double MaxDirectStitchMm { get; init; } = 3.0;

    /// <summary>Gaps longer than this are cut: tie-off, trim, jump, tie-in.</summary>
    public double TrimAboveMm { get; init; } = 7.0;

    /// <summary>Always trim before a colour change.</summary>
    public bool TrimOnColorChange { get; init; } = true;

    /// <summary>
    /// Replace a jump/trim by a running stitch when the whole way lies under objects that are
    /// sewn later (their stitching hides it). Mirrors how the FER-7 reference links its motifs.
    /// </summary>
    public bool HiddenTravel { get; init; } = true;

    /// <summary>Insert tie-in/tie-off intents around trims, colour changes and design start/end.</summary>
    public bool TieStitches { get; init; } = true;

    public static readonly ConnectionPolicy Default = new();
}
