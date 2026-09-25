namespace Embroidery.Core.Objects;

public sealed record RunParameters
{
    public double StitchLengthMm { get; init; } = 2.5;

    /// <summary>Corners sharper than this (deviation from straight, degrees) get a forced penetration.</summary>
    public double CornerAngleDeg { get; init; } = 30;

    /// <summary>1 = single run, 3 = triple ("bean") run. Must be odd so the path ends at its end.</summary>
    public int Repeats { get; init; } = 1;
}

public enum ShortStitchMode
{
    None,
    InnerOnly,
}

/// <summary>How a centre-line column is joined where it turns sharply.</summary>
public enum CornerStyle
{
    /// <summary>Miter up to 100° of turn, lap up to 140°, cap beyond.</summary>
    Auto,

    /// <summary>The first piece runs on past the corner and the second is sewn over it.</summary>
    Lap,

    /// <summary>Both pieces meet on the bisector of the corner.</summary>
    Miter,

    /// <summary>Both pieces stop at the corner; a very sharp tip is cut off flat.</summary>
    Cap,
}

public sealed record SatinParameters
{
    /// <summary>
    /// Distance between consecutive penetrations on the same rail, measured on whichever
    /// (compensated) rail advances further — the outside of a curve. Threads crossing the
    /// column are therefore <c>SpacingMm / 2</c> apart. Reference: FER-7 uses ≈0.30 mm.
    /// </summary>
    public double SpacingMm { get; init; } = 0.4;

    /// <summary>Total outward widening of each throw (split half per side).</summary>
    public double PullCompensationMm { get; init; } = 0.2;

    /// <summary>Column ends are pulled inward by this amount along the column.</summary>
    public double PushCompensationMm { get; init; } = 0.0;

    /// <summary>Throws wider than this are split into shorter stitches.</summary>
    public double MaxWidthMm { get; init; } = 8.0;

    public ShortStitchMode ShortStitch { get; init; } = ShortStitchMode.InnerOnly;

    /// <summary>
    /// A centre-line column turning more sharply than this (degrees) at one point is split there
    /// into two overlapping columns ("lap" corner) instead of folding. 180 disables splitting.
    /// </summary>
    public double CornerSplitAngleDeg { get; init; } = 60;

    public CornerStyle CornerStyle { get; init; } = CornerStyle.Auto;

    /// <summary>Penetrations closer than this on the inner rail of a curve are shortened.</summary>
    public double ShortStitchThresholdMm { get; init; } = 0.25;

    /// <summary>How far (fraction of the throw) shortened penetrations move towards the other rail.</summary>
    public double ShortStitchFraction { get; init; } = 0.25;

    public SatinUnderlay Underlay { get; init; } = new();
}

public sealed record SatinUnderlay
{
    public bool CenterWalk { get; init; } = true;
    public bool EdgeWalk { get; init; } = false;
    public bool ZigZag { get; init; } = false;
    public double EdgeInsetMm { get; init; } = 0.4;
    public double ZigZagSpacingMm { get; init; } = 2.0;
    public double StitchLengthMm { get; init; } = 2.5;
}

public sealed record TatamiParameters
{
    /// <summary>Stitch direction in degrees; 0 = rows along +X.</summary>
    public double AngleDeg { get; init; } = 45;

    public double RowSpacingMm { get; init; } = 0.4;
    public double StitchLengthMm { get; init; } = 4.0;

    /// <summary>Penetration offset between consecutive rows as a fraction of the stitch length.</summary>
    public double StaggerFraction { get; init; } = 1.0 / 3.0;

    /// <summary>Row ends are pulled in from the boundary by this amount.</summary>
    public double EdgeInsetMm { get; init; } = 0.0;

    /// <summary>Row ends are extended along the stitch direction by this amount.</summary>
    public double PullCompensationMm { get; init; } = 0.2;

    /// <summary>Stitches shorter than this at row ends are merged into the neighbour.</summary>
    public double MinStitchLengthMm { get; init; } = 0.8;

    public TatamiUnderlay Underlay { get; init; } = new();
}

public sealed record TatamiUnderlay
{
    public bool EdgeRun { get; init; } = true;
    public bool Fill { get; init; } = true;
    public double InsetMm { get; init; } = 0.8;
    public double RowSpacingMm { get; init; } = 2.0;
    public double StitchLengthMm { get; init; } = 3.0;
}

public enum TwistDirection
{
    /// <summary>Strands rise from the left edge to the right edge of the band.</summary>
    S,

    /// <summary>Mirror of S.</summary>
    Z,
}

public sealed record RopeParameters
{
    /// <summary>Distance along the path between consecutive strands.</summary>
    public double PitchMm { get; init; } = 3.0;

    /// <summary>How far along the path one strand travels while crossing the band (slant).</summary>
    public double StrandLengthMm { get; init; } = 6.0;

    /// <summary>Satin density inside each strand (same-rail spacing).</summary>
    public double SpacingMm { get; init; } = 0.35;

    /// <summary>Strand thickness relative to the gap between strands; above 1 they overlap.</summary>
    public double OverlapFactor { get; init; } = 1.15;

    public TwistDirection Twist { get; init; } = TwistDirection.S;

    public double PullCompensationMm { get; init; } = 0.15;

    /// <summary>A running stitch along the band centre under the strands.</summary>
    public bool CenterUnderlay { get; init; } = true;
}
