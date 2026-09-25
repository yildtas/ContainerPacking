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

    /// <summary>A ladder from explicit facing pairs (used by generated shapes such as rope strands).</summary>
    public static SatinLadder FromPairs(Vec2[] a, Vec2[] b)
    {
        if (a.Length != b.Length || a.Length < 2) throw new ArgumentException("A ladder needs at least two matching pairs.");
        return new SatinLadder(a, b);
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
    /// Moves every point lying on the wrong side of the line through <paramref name="c"/> (normal
    /// <paramref name="t"/>) back onto it, sliding along <paramref name="slide"/>. Points with
    /// <c>dot(p − c, t) &gt; 0</c> are clipped when <paramref name="keepNegative"/> is true.
    /// Used to make two corner pieces meet exactly on the miter line.
    /// </summary>
    public SatinLadder ClipAtLine(Vec2 c, Vec2 t, bool keepNegative, Vec2 slide)
    {
        var denom = Vec2.Dot(slide, t);
        if (Math.Abs(denom) < 1e-9) return this;
        Vec2 Clip(Vec2 p)
        {
            var side = Vec2.Dot(p - c, t);
            var wrong = keepNegative ? side > 0 : side < 0;
            return wrong ? p - slide * (side / denom) : p;
        }

        var a = new List<Vec2>(A.Length);
        var b = new List<Vec2>(B.Length);
        for (var i = 0; i < A.Length; i++)
        {
            var pa = Clip(A[i]);
            var pb = Clip(B[i]);
            if (a.Count > 0 && pa.ApproximatelyEquals(a[^1], 1e-6) && pb.ApproximatelyEquals(b[^1], 1e-6)) continue;
            a.Add(pa);
            b.Add(pb);
        }

        return a.Count >= 2 ? new SatinLadder(a.ToArray(), b.ToArray()) : this;
    }

    /// <summary>
    /// The columns actually sewn. A centre-line column is split at sharp corners; each corner is
    /// joined according to <see cref="SatinParameters.CornerStyle"/> (Auto: miter up to 100° of
    /// turn, lap up to 140°, cap beyond). A rails column is one piece. Code SAT006 reports splits.
    /// </summary>
    public static GenerationResult<IReadOnlyList<SatinLadder>> BuildColumns(SatinObject item)
    {
        if (item.Source != SatinSource.Stroke || item.Parameters.CornerSplitAngleDeg >= 180 || item.Centerline.Count < 3)
        {
            var single = Build(item);
            return new(single.Value is { } l ? [l] : [], single.Diagnostics);
        }

        var path = new ArcLengthPath(item.Centerline);
        var window = Math.Max(0.5, item.WidthMm / 2);
        var cornerPositions = FindCorners(path, item.Parameters.CornerSplitAngleDeg, window);
        if (cornerPositions.Count == 0 || path.Start.ApproximatelyEquals(path.End, 1e-6))
        {
            var single = Build(item);
            return new(single.Value is { } l ? [l] : [], single.Diagnostics);
        }

        var half = item.WidthMm / 2;
        var corners = cornerPositions.Select(s =>
        {
            var c = path.PointAt(s);
            var d1 = (c - path.PointAt(s - window)).Normalized();
            var d2 = (path.PointAt(s + window) - c).Normalized();
            var turnDeg = Math.Acos(Math.Clamp(Vec2.Dot(d1, d2), -1, 1)) * 180 / Math.PI;
            var style = item.Parameters.CornerStyle switch
            {
                CornerStyle.Auto => turnDeg <= 100 ? CornerStyle.Miter : turnDeg <= 140 ? CornerStyle.Lap : CornerStyle.Cap,
                var fixedStyle => fixedStyle,
            };
            // How far the outer corner lies beyond the centre-line corner along each leg.
            var reach = Math.Min(half * Math.Tan(turnDeg * Math.PI / 360), item.WidthMm * 2) + 0.3;
            return (S: s, C: c, D1: d1, D2: d2, Style: style, Reach: reach, T: (d1 + d2).Normalized());
        }).ToList();

        var diagnostics = new List<Diagnostic>();
        var ladders = new List<SatinLadder>();
        var cuts = new List<double> { 0 };
        cuts.AddRange(cornerPositions);
        cuts.Add(path.Length);
        for (var k = 0; k + 1 < cuts.Count; k++)
        {
            var piece = SubPath(path, cuts[k], cuts[k + 1]);
            var startCorner = k > 0 ? corners[k - 1] : default;
            var endCorner = k < corners.Count ? corners[k] : default;
            var hasStart = k > 0;
            var hasEnd = k < corners.Count;

            if (hasStart && startCorner.Style == CornerStyle.Miter)
            {
                piece.Insert(0, piece[0] - startCorner.D2 * startCorner.Reach);
            }

            if (hasEnd)
            {
                switch (endCorner.Style)
                {
                    case CornerStyle.Lap:
                        piece.Add(piece[^1] + endCorner.D1 * half);
                        break;
                    case CornerStyle.Miter:
                        piece.Add(piece[^1] + endCorner.D1 * endCorner.Reach);
                        break;
                }
            }

            var part = item with
            {
                Centerline = piece,
                StartTaperMm = k == 0 ? item.StartTaperMm : 0,
                EndTaperMm = k == cuts.Count - 2 ? item.EndTaperMm : 0,
            };
            var built = Build(part);
            diagnostics.AddRange(built.Diagnostics);
            if (built.Value is not { } ladder) continue;
            if (hasEnd && endCorner.Style == CornerStyle.Miter)
            {
                ladder = ladder.ClipAtLine(endCorner.C, endCorner.T, keepNegative: true, endCorner.D1);
            }

            if (hasStart && startCorner.Style == CornerStyle.Miter)
            {
                ladder = ladder.ClipAtLine(startCorner.C, startCorner.T, keepNegative: false, startCorner.D2);
            }

            ladders.Add(ladder);
        }

        var summary = string.Join(", ", corners.GroupBy(c => c.Style).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
        diagnostics.Add(Diagnostic.Info("SAT006", $"Column split at {corners.Count} sharp corner(s): {summary}.", item.Id));
        return new(ladders, diagnostics);
    }

    /// <summary>
    /// Arc-length positions where the line turns more than <paramref name="angleDeg"/>, judged over a
    /// window so that densely flattened curves are not mistaken for corners.
    /// </summary>
    private static List<double> FindCorners(ArcLengthPath path, double angleDeg, double window)
    {
        var cosLimit = Math.Cos(angleDeg * Math.PI / 180);
        var corners = new List<double>();
        for (var i = 1; i < path.Points.Count - 1; i++)
        {
            var s = path.LengthAt(i);
            if (s < window || s > path.Length - window) continue;
            var p = path.Points[i];
            var din = (p - path.PointAt(s - window)).Normalized();
            var dout = (path.PointAt(s + window) - p).Normalized();
            if (Vec2.Dot(din, dout) >= cosLimit) continue;

            // A real corner turns within a tiny neighbourhood too; a tight but smooth curve
            // (spiral centre) does not, and must stay one column.
            const double near = 0.12;
            var nin = (p - path.PointAt(s - near)).Normalized();
            var nout = (path.PointAt(s + near) - p).Normalized();
            if (Vec2.Dot(nin, nout) >= Math.Cos(angleDeg * 0.8 * Math.PI / 180)) continue;
            if (corners.Count > 0 && s - corners[^1] < 2 * window)
            {
                continue;
            }

            corners.Add(s);
        }

        return corners;
    }

    private static List<Vec2> SubPath(ArcLengthPath path, double from, double to)
    {
        var result = new List<Vec2> { path.PointAt(from) };
        for (var i = 0; i < path.Points.Count; i++)
        {
            var s = path.LengthAt(i);
            if (s > from + 1e-9 && s < to - 1e-9) result.Add(path.Points[i]);
        }

        result.Add(path.PointAt(to));
        return result;
    }

    /// <summary>
    /// Builds the ladder from the object's authoritative geometry.
    /// Codes: SAT001 invalid geometry, SAT002 rail direction fixed, SAT004 column folds on a tight
    /// curve, SAT005 rung ignored, SAT007 column twists.
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

        // Consecutive throws that cross each other mean the column twists (typically at a
        // corner drawn with rails but no rungs): the stitches there would pile up and bunch.
        var twists = 0;
        for (var i = 1; i < a.Count; i++)
        {
            if (SegmentsCross(a[i - 1], b[i - 1], a[i], b[i])) twists++;
        }

        if (twists > 0)
        {
            diagnostics.Add(Diagnostic.Warning("SAT007",
                $"The column twists in {twists} place(s): throws cross each other. Add rungs there or split the column.", item.Id));
        }

        return new SatinLadder(a.ToArray(), b.ToArray());
    }

    private static bool SegmentsCross(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        static double Orient(Vec2 a, Vec2 b, Vec2 c) => Vec2.Cross(b - a, c - a);
        var d1 = Orient(q1, q2, p1);
        var d2 = Orient(q1, q2, p2);
        var d3 = Orient(p1, p2, q1);
        var d4 = Orient(p1, p2, q2);
        // Proper crossings only; touching at a shared tip point (pointed ends) is not a twist.
        return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
    }
}
