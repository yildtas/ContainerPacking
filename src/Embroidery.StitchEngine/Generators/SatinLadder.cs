using Embroidery.Core.Diagnostics;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;

namespace Embroidery.StitchEngine.Generators;

/// <summary>
/// A satin column resolved into densely sampled facing point pairs ("rungs") A[i] ↔ B[i].
/// Both source modes (rails+rungs, centre line+width) reduce to this, so spacing, compensation,
/// short stitches and underlay are implemented once. <see cref="Advance"/> is the cumulative
/// distance travelled by whichever rail moves further between neighbouring pairs, i.e. the
/// outside of a curve, which is where satin gaps would appear.
/// </summary>
public sealed class SatinLadder
{
    /// <summary>Resolution of the sampled ladder.</summary>
    public const double SampleStepMm = 0.25;

    private SatinLadder(Vec2[] a, Vec2[] b)
    {
        A = a;
        B = b;
        Advance = new double[a.Length];
        for (var i = 1; i < a.Length; i++)
        {
            Advance[i] = Advance[i - 1] + Math.Max(Vec2.Distance(a[i - 1], a[i]), Vec2.Distance(b[i - 1], b[i]));
        }
    }

    public Vec2[] A { get; }
    public Vec2[] B { get; }
    public double[] Advance { get; }
    public double Length => Advance[^1];
    public double MaxWidth => A.Zip(B, Vec2.Distance).Max();

    /// <summary>Facing pair at advance <paramref name="s"/> (linear between samples).</summary>
    public (Vec2 A, Vec2 B) At(double s)
    {
        if (s <= 0 || A.Length == 1) return (A[0], B[0]);
        if (s >= Length) return (A[^1], B[^1]);
        var i = Array.BinarySearch(Advance, s);
        if (i >= 0) return (A[i], B[i]);
        i = ~i - 1;
        var span = Advance[i + 1] - Advance[i];
        var t = span <= 0 ? 0 : (s - Advance[i]) / span;
        return (Vec2.Lerp(A[i], A[i + 1], t), Vec2.Lerp(B[i], B[i + 1], t));
    }

    public SatinLadder Reversed() => new(A.Reverse().ToArray(), B.Reverse().ToArray());

    /// <summary>Widens every pair across the column by <paramref name="totalMm"/> (half per side).</summary>
    public SatinLadder WithPullCompensation(double totalMm)
    {
        if (Math.Abs(totalMm) < 1e-9) return this;
        var a = new Vec2[A.Length];
        var b = new Vec2[B.Length];
        for (var i = 0; i < A.Length; i++)
        {
            var u = (B[i] - A[i]).Normalized();
            // Never let negative compensation flip a narrow pair inside out.
            var half = Math.Max(totalMm / 2, -Vec2.Distance(A[i], B[i]) / 2 + 0.05);
            a[i] = A[i] - u * half;
            b[i] = B[i] + u * half;
        }

        return new SatinLadder(a, b);
    }

    /// <summary>
    /// Builds the ladder from the object's authoritative geometry.
    /// Codes: SAT001 invalid geometry, SAT002 rail direction fixed, SAT004 column folds on a tight
    /// curve, SAT005 rung ignored.
    /// </summary>
    public static GenerationResult<SatinLadder?> Build(SatinObject item)
    {
        var diagnostics = new List<Diagnostic>();
        var ladder = item.Source == SatinSource.Stroke
            ? FromStroke(item, diagnostics)
            : FromRails(item, diagnostics);
        return new(ladder, diagnostics);
    }

    private static SatinLadder? FromStroke(SatinObject item, List<Diagnostic> diagnostics)
    {
        if (item.Centerline.Count < 2 || item.WidthMm <= 0)
        {
            diagnostics.Add(Diagnostic.Error("SAT001", "Satin stroke needs a centre line of at least two points and a positive width.", item.Id));
            return null;
        }

        var path = new ArcLengthPath(item.Centerline);
        if (path.Length < 1e-6)
        {
            diagnostics.Add(Diagnostic.Error("SAT001", "Satin centre line has zero length.", item.Id));
            return null;
        }

        var closed = path.Start.ApproximatelyEquals(path.End, 1e-6);
        var n = Math.Max(2, (int)Math.Ceiling(path.Length / SampleStepMm));
        var c = new Vec2[n + 1];
        for (var i = 0; i <= n; i++) c[i] = path.PointAt(path.Length * i / n);

        var a = new Vec2[n + 1];
        var b = new Vec2[n + 1];
        var folds = 0;
        for (var i = 0; i <= n; i++)
        {
            // Central differences on the evenly resampled line give smooth normals through curves.
            var prev = i > 0 ? c[i - 1] : closed ? c[n - 1] : c[i];
            var next = i < n ? c[i + 1] : closed ? c[1] : c[i];
            var tangent = (next - prev).Normalized();
            var normal = tangent.Perpendicular;
            var s = path.Length * i / n;
            var taper = 1.0;
            if (!closed && item.StartTaperMm > 0) taper = Math.Min(taper, s / item.StartTaperMm);
            if (!closed && item.EndTaperMm > 0) taper = Math.Min(taper, (path.Length - s) / item.EndTaperMm);
            var half = Math.Max(0.15, item.WidthMm * Math.Clamp(taper, 0, 1)) / 2;
            a[i] = c[i] - normal * half;
            b[i] = c[i] + normal * half;

            // Curvature check: the inner edge folds when the radius is below half the width.
            if (i > 0 && i < n)
            {
                var turn = Math.Abs(Vec2.Cross((c[i] - c[i - 1]).Normalized(), (c[i + 1] - c[i]).Normalized()));
                var curvature = turn / Math.Max(1e-9, path.Length / n);
                if (curvature * half > 0.95) folds++;
            }
        }

        if (folds > 0)
        {
            diagnostics.Add(Diagnostic.Warning("SAT004",
                FormattableString.Invariant($"The column is wider than a tight curve allows ({folds} spot(s)); the inner edge folds. Reduce the width or split the column there."),
                item.Id));
        }

        return new SatinLadder(a, b);
    }

    private static SatinLadder? FromRails(SatinObject item, List<Diagnostic> diagnostics)
    {
        if (item.RailA.Count < 2 || item.RailB.Count < 2)
        {
            diagnostics.Add(Diagnostic.Error("SAT001", "Satin needs two rails with at least two points each.", item.Id));
            return null;
        }

        var railA = new ArcLengthPath(item.RailA);
        var railB = new ArcLengthPath(item.RailB);
        if (railA.Length < 1e-6 || railB.Length < 1e-6)
        {
            diagnostics.Add(Diagnostic.Error("SAT001", "Satin rails must have non-zero length.", item.Id));
            return null;
        }

        var straight = Vec2.Distance(railA.Start, railB.Start) + Vec2.Distance(railA.End, railB.End);
        var crossed = Vec2.Distance(railA.Start, railB.End) + Vec2.Distance(railA.End, railB.Start);
        if (crossed < straight)
        {
            railB = railB.Reversed();
            diagnostics.Add(Diagnostic.Info("SAT002", "Rails ran in opposite directions; rail B was reversed.", item.Id));
        }

        // Anchors (arc length on A, arc length on B): each rung pins a facing pair. The endpoint
        // nearer to rail A is taken as its A side, so rungs may be drawn in either direction.
        var rungAnchors = new List<(double SA, double SB)>();
        foreach (var rung in item.Rungs)
        {
            var (pa1, da1) = railA.Project(rung.A);
            var (pa2, da2) = railA.Project(rung.B);
            var (sa, other) = da1 <= da2 ? (pa1, rung.B) : (pa2, rung.A);
            rungAnchors.Add((sa, railB.Project(other).S));
        }

        // Keep only rungs that move forward on both rails; crossing rungs would twist the column.
        const double eps = 1e-6;
        var monotonic = new List<(double SA, double SB)> { (0, 0) };
        var ignored = 0;
        foreach (var anchor in rungAnchors.OrderBy(x => x.SA))
        {
            var last = monotonic[^1];
            var inside = anchor.SA < railA.Length - eps && anchor.SB < railB.Length - eps;
            if (inside && anchor.SA > last.SA + eps && anchor.SB > last.SB + eps) monotonic.Add(anchor);
            else ignored++;
        }

        monotonic.Add((railA.Length, railB.Length));

        if (ignored > 0)
        {
            diagnostics.Add(Diagnostic.Warning("SAT005",
                $"{ignored} rung(s) cross other rungs or run backwards and were ignored.", item.Id));
        }

        var a = new List<Vec2> { railA.Start };
        var b = new List<Vec2> { railB.Start };
        for (var k = 1; k < monotonic.Count; k++)
        {
            var (a0, b0) = monotonic[k - 1];
            var (a1, b1) = monotonic[k];
            var m = Math.Max(1, (int)Math.Ceiling(Math.Max(a1 - a0, b1 - b0) / SampleStepMm));
            for (var j = 1; j <= m; j++)
            {
                var t = (double)j / m;
                a.Add(railA.PointAt(a0 + (a1 - a0) * t));
                b.Add(railB.PointAt(b0 + (b1 - b0) * t));
            }
        }

        return new SatinLadder(a.ToArray(), b.ToArray());
    }
}
