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

public sealed record SatinParameters
{
    /// <summary>Distance between consecutive zig-zag penetrations along the column.</summary>
    public double SpacingMm { get; init; } = 0.4;

    /// <summary>Total outward widening of each throw (split half per side).</summary>
    public double PullCompensationMm { get; init; } = 0.2;

    /// <summary>Column ends are pulled inward by this amount along the column.</summary>
    public double PushCompensationMm { get; init; } = 0.0;

    /// <summary>Throws wider than this are split into shorter stitches.</summary>
    public double MaxWidthMm { get; init; } = 8.0;

    public ShortStitchMode ShortStitch { get; init; } = ShortStitchMode.InnerOnly;

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
