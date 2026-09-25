using Embroidery.Machine;

namespace Embroidery.Application.Analysis;

public sealed record Percentiles(double P5, double P25, double P50, double P75, double P95, double Max)
{
    public static Percentiles? Of(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(v => v).ToArray();
        double At(double q) => sorted[(int)Math.Clamp(Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return new Percentiles(At(0.05), At(0.25), At(0.5), At(0.75), At(0.95), sorted[^1]);
    }
}

/// <summary>
/// Format-level and stitch-behaviour metrics of a machine stream, used to compare our output with
/// a reference (e.g. a Wilcom/Pulse DST). Values marked "inferred" are interpretations of the raw
/// stream (a DST has no object, satin or trim records) and must be reported as such.
/// </summary>
public sealed record StitchMetrics
{
    public required int Records { get; init; }
    public required int Stitches { get; init; }
    public required int Jumps { get; init; }
    public required int ColorChanges { get; init; }
    public required double WidthMm { get; init; }
    public required double HeightMm { get; init; }
    public required double ThreadPathM { get; init; }

    /// <summary>Stitch length distribution (mm), zero-length moves excluded.</summary>
    public required Percentiles? StitchLengthMm { get; init; }

    /// <summary>Counts of stitches per length band: &lt;0.3, 0.3–0.5, 0.5–1, 1–2, 2–3, 3–4, 4–5, 5–7, 7–10, ≥10 mm.</summary>
    public required int[] StitchLengthBands { get; init; }

    public required int JumpRuns { get; init; }

    /// <summary>Inferred: runs of at least <see cref="TrimJumpThreshold"/> consecutive jumps.</summary>
    public required int InferredTrims { get; init; }

    public int TrimJumpThreshold { get; init; } = 3;

    /// <summary>Inferred: moves that reverse direction (satin throws) longer than 1.2 mm — column width.</summary>
    public required Percentiles? SatinThrowMm { get; init; }

    /// <summary>Inferred: distance between penetrations two apart inside zig-zag runs — same-rail spacing.</summary>
    public required Percentiles? SatinSameRailSpacingMm { get; init; }

    /// <summary>Inferred: share of stitches in ≥5-stitch straight runs (underlay, travel, run objects).</summary>
    public required double RunningShare { get; init; }

    public required Percentiles? RunningStitchMm { get; init; }

    public static readonly double[] BandEdges = [0.3, 0.5, 1, 2, 3, 4, 5, 7, 10];

    public static StitchMetrics From(EncodedStitchPlan plan, double unitsPerMm = 10)
    {
        var s = plan.Stitches;
        var moves = new List<(double Dx, double Dy)>();
        int jumps = 0, colors = 0, stitches = 0, jumpRuns = 0, trims = 0, currentRun = 0;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        var any = false;
        int px = 0, py = 0;
        double thread = 0;
        foreach (var st in s)
        {
            switch (st.Command)
            {
                case EncodedCommand.Stitch:
                {
                    stitches++;
                    CloseJumpRun();
                    double dx = (st.X - px) / unitsPerMm, dy = (st.Y - py) / unitsPerMm;
                    if (dx != 0 || dy != 0) moves.Add((dx, dy));
                    thread += Math.Sqrt(dx * dx + dy * dy);
                    if (!any) { minX = maxX = st.X; minY = maxY = st.Y; any = true; }
                    minX = Math.Min(minX, st.X); maxX = Math.Max(maxX, st.X);
                    minY = Math.Min(minY, st.Y); maxY = Math.Max(maxY, st.Y);
                    break;
                }
                case EncodedCommand.Jump:
                    jumps++;
                    currentRun++;
                    // A jump interrupts the stitch sequence for direction analysis.
                    moves.Add((double.NaN, double.NaN));
                    break;
                case EncodedCommand.ColorChange:
                    colors++;
                    CloseJumpRun();
                    moves.Add((double.NaN, double.NaN));
                    break;
                case EncodedCommand.Trim:
                    // An explicit trim (non-DST formats) counts like an inferred one.
                    trims++;
                    moves.Add((double.NaN, double.NaN));
                    break;
            }

            px = st.X;
            py = st.Y;
        }

        CloseJumpRun();

        var lengths = moves.Where(m => !double.IsNaN(m.Dx)).Select(m => Math.Sqrt(m.Dx * m.Dx + m.Dy * m.Dy)).ToList();
        var bands = new int[BandEdges.Length + 1];
        foreach (var l in lengths) bands[Array.FindIndex(BandEdges, e => l < e) is var i && i >= 0 ? i : BandEdges.Length]++;

        // Direction labels between consecutive moves: R = reverses (zig-zag), F = continues.
        var throws = new List<double>();
        var sameRail = new List<double>();
        var runningStitches = new List<double>();
        var runLength = 0;
        var runningCount = 0;
        var runBuffer = new List<double>();
        for (var k = 1; k < moves.Count; k++)
        {
            var (ax, ay) = moves[k - 1];
            var (bx, by) = moves[k];
            if (double.IsNaN(ax) || double.IsNaN(bx))
            {
                FlushRun();
                continue;
            }

            var la = Math.Sqrt(ax * ax + ay * ay);
            var lb = Math.Sqrt(bx * bx + by * by);
            var cos = (ax * bx + ay * by) / (la * lb);
            if (cos < -0.5)
            {
                FlushRun();
                if (lb > 1.2) throws.Add(lb);
                // Two consecutive reversals: the penetration two back is on the same rail.
                if (k >= 2 && !double.IsNaN(moves[k - 2].Dx))
                {
                    var (cx, cy) = moves[k - 2];
                    var lc = Math.Sqrt(cx * cx + cy * cy);
                    if ((ax * cx + ay * cy) / (la * lc) < -0.5)
                    {
                        var d = Math.Sqrt((ax + bx) * (ax + bx) + (ay + by) * (ay + by));
                        if (d < 3) sameRail.Add(d);
                    }
                }
            }
            else if (cos > 0.5)
            {
                if (runLength == 0) runBuffer.Add(la);
                runLength++;
                runBuffer.Add(lb);
            }
            else
            {
                FlushRun();
            }
        }

        FlushRun();

        var width = any ? (maxX - minX) / unitsPerMm : 0;
        var height = any ? (maxY - minY) / unitsPerMm : 0;
        return new StitchMetrics
        {
            Records = s.Count,
            Stitches = stitches,
            Jumps = jumps,
            ColorChanges = colors,
            WidthMm = width,
            HeightMm = height,
            ThreadPathM = thread / 1000,
            StitchLengthMm = Percentiles.Of(lengths),
            StitchLengthBands = bands,
            JumpRuns = jumpRuns,
            InferredTrims = trims,
            SatinThrowMm = Percentiles.Of(throws),
            SatinSameRailSpacingMm = Percentiles.Of(sameRail),
            RunningShare = lengths.Count == 0 ? 0 : (double)runningCount / lengths.Count,
            RunningStitchMm = Percentiles.Of(runningStitches),
        };

        void CloseJumpRun()
        {
            if (currentRun == 0) return;
            jumpRuns++;
            if (currentRun >= 3) trims++;
            currentRun = 0;
        }

        void FlushRun()
        {
            // A straight run of at least 5 stitches (4 continuing direction changes).
            if (runLength >= 4)
            {
                runningCount += runBuffer.Count;
                runningStitches.AddRange(runBuffer);
            }

            runLength = 0;
            runBuffer.Clear();
        }
    }
}
