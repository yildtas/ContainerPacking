using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;

namespace Embroidery.Application.Projects;

/// <summary>Rejects parameter values that are physically meaningless or would explode stitch counts.</summary>
public static class ParameterValidator
{
    public static void Validate(EmbroideryObject item)
    {
        switch (item)
        {
            case RunObject r:
                Points(r.Path, "path");
                Range(r.Parameters.StitchLengthMm, 0.5, 12, "run stitch length");
                Range(r.Parameters.CornerAngleDeg, 0, 180, "corner angle");
                Range(r.Parameters.Repeats, 1, 9, "repeats");
                break;
            case SatinObject s:
                Points(s.RailA, "rail A");
                Points(s.RailB, "rail B");
                var sp = s.Parameters;
                Range(sp.SpacingMm, 0.15, 5, "satin spacing");
                Range(sp.PullCompensationMm, -1, 3, "pull compensation");
                Range(sp.PushCompensationMm, 0, 5, "push compensation");
                Range(sp.MaxWidthMm, 1, 20, "max satin width");
                Range(sp.ShortStitchThresholdMm, 0, 2, "short stitch threshold");
                Range(sp.ShortStitchFraction, 0, 0.5, "short stitch fraction");
                Range(sp.Underlay.EdgeInsetMm, 0, 5, "edge inset");
                Range(sp.Underlay.ZigZagSpacingMm, 0.5, 10, "zig-zag spacing");
                Range(sp.Underlay.StitchLengthMm, 0.5, 12, "underlay stitch length");
                break;
            case TatamiObject t:
                foreach (var ring in t.Region.Rings) Points(ring, "region");
                var tp = t.Parameters;
                Range(tp.AngleDeg, -360, 360, "fill angle");
                Range(tp.RowSpacingMm, 0.15, 5, "row spacing");
                Range(tp.StitchLengthMm, 0.5, 12, "fill stitch length");
                Range(tp.StaggerFraction, 0, 1, "stagger fraction");
                Range(tp.EdgeInsetMm, 0, 5, "edge inset");
                Range(tp.PullCompensationMm, -1, 3, "pull compensation");
                Range(tp.MinStitchLengthMm, 0, 5, "minimum stitch length");
                Range(tp.Underlay.InsetMm, 0, 10, "underlay inset");
                Range(tp.Underlay.RowSpacingMm, 0.5, 10, "underlay row spacing");
                Range(tp.Underlay.StitchLengthMm, 0.5, 12, "underlay stitch length");
                break;
        }
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            throw new DesignValidationException(FormattableString.Invariant($"The {name} must be between {min} and {max} (got {value})."));
        }
    }

    private static void Points(IReadOnlyList<Vec2> points, string name)
    {
        if (points.Count > 200_000) throw new DesignValidationException($"The {name} has too many points.");
        foreach (var p in points)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 10_000 || Math.Abs(p.Y) > 10_000)
            {
                throw new DesignValidationException($"The {name} contains an invalid coordinate.");
            }
        }
    }
}
