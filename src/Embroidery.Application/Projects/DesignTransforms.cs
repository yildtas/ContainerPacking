using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;

namespace Embroidery.Application.Projects;

public enum MirrorAxis
{
    /// <summary>Left ↔ right (e.g. left front panel → right front panel).</summary>
    Horizontal,

    /// <summary>Top ↔ bottom.</summary>
    Vertical,
}

/// <summary>Whole-design geometric transforms that keep every object editable.</summary>
public static class DesignTransforms
{
    /// <summary>
    /// Mirrors every object about the centre of the design. Fill angles are mirrored with the
    /// geometry and rope twists swap S ↔ Z, so the result is the true mirror image.
    /// </summary>
    public static Design Mirror(Design design, MirrorAxis axis)
    {
        var bounds = Bounds.Empty;
        foreach (var o in design.Objects) bounds = bounds.Include(o.Bounds);
        if (bounds.IsEmpty) return design;
        var c = bounds.Center;
        Vec2 M(Vec2 p) => axis == MirrorAxis.Horizontal ? new Vec2(2 * c.X - p.X, p.Y) : new Vec2(p.X, 2 * c.Y - p.Y);
        IReadOnlyList<Vec2> L(IReadOnlyList<Vec2> pts) => pts.Select(M).ToArray();

        EmbroideryObject Transform(EmbroideryObject o)
        {
            var moved = o switch
            {
                RunObject r => (EmbroideryObject)(r with { Path = L(r.Path) }),
                SatinObject s => s with
                {
                    RailA = L(s.RailA),
                    RailB = L(s.RailB),
                    Rungs = s.Rungs.Select(g => new Rung(M(g.A), M(g.B))).ToArray(),
                    Centerline = L(s.Centerline),
                },
                TatamiObject t => t with
                {
                    Region = t.Region with { Rings = t.Region.Rings.Select(L).ToArray() },
                    // A direction θ mirrors to −θ (fill rows have no orientation).
                    Parameters = t.Parameters with { AngleDeg = NormalizeAngle(-t.Parameters.AngleDeg) },
                },
                RopeObject rope => rope with
                {
                    Path = L(rope.Path),
                    Parameters = rope.Parameters with
                    {
                        Twist = rope.Parameters.Twist == TwistDirection.S ? TwistDirection.Z : TwistDirection.S,
                    },
                },
                _ => o,
            };
            return moved with { EntryPoint = o.EntryPoint is { } e ? M(e) : null };
        }

        return design with { Objects = design.Objects.Select(Transform).ToList() };
    }

    private static double NormalizeAngle(double deg)
    {
        deg %= 180;
        if (deg <= -90) deg += 180;
        if (deg > 90) deg -= 180;
        return deg;
    }
}
