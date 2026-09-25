using Embroidery.Application.Projects;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.Machine;

namespace Embroidery.Application.Analysis;

public sealed record TraceOptions
{
    /// <summary>Total width the original digitizer added to satin columns; removed from the traced rails.</summary>
    public double PullCompensationMm { get; init; }

    /// <summary>Fewer zig-zag throws than this are not treated as a satin column.</summary>
    public int MinSatinThrows { get; init; } = 8;

    /// <summary>Zig-zags with a wider same-rail spacing are underlay, not a top satin.</summary>
    public double MaxSatinSpacingMm { get; init; } = 1.0;

    /// <summary>Shortest move counted as a satin throw.</summary>
    public double MinThrowMm { get; init; } = 0.6;

    public double UnitsPerMm { get; init; } = 10;
}

public sealed record TraceResult(Design Design, int SatinColumns, int RunObjects, int DroppedStitches, int TotalStitches, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Recovers editable vector objects from a machine file (the reverse of stitch generation), so a
/// design that exists only as DST can be re-digitized: every zig-zag run of satin throws becomes a
/// rails-and-rungs satin column (penetrations alternate between the rails), straight running
/// stitches outside the columns become run objects, and running stitches under the columns
/// (underlay, hidden travel) and tie-ins are dropped. DST carries no colours, object boundaries or
/// parameters, so colours are placeholders and fills come back as run lines (TRC002).
/// Codes: TRC001 summary, TRC002 many stitches kept as runs (probably fills).
/// </summary>
public static class DstTracer
{
    private static readonly string[] Palette = ["#D4A53C", "#1F4E9C", "#B22222", "#2E8B57", "#6A3D9A", "#E07B00", "#222222", "#8B4513"];

    private sealed record Piece(int Sequence, int Start, int Thread, bool Satin, List<Vec2> Points);

    public static TraceResult Trace(EncodedStitchPlan plan, string name, TraceOptions? options = null)
    {
        options ??= new TraceOptions();
        var sequences = Sequences(plan, options.UnitsPerMm);
        var total = sequences.Sum(s => s.Points.Count);

        // 1. Split every sewn sequence into satin runs and running pieces.
        var pieces = new List<Piece>();
        for (var si = 0; si < sequences.Count; si++)
        {
            var (thread, pts) = sequences[si];
            var satinRanges = SatinRanges(pts, options);
            var cursor = 0;
            foreach (var (from, to) in satinRanges)
            {
                if (from > cursor) pieces.Add(new Piece(si, cursor, thread, false, pts.GetRange(cursor, from - cursor + 1)));
                pieces.Add(new Piece(si, from, thread, true, pts.GetRange(from, to - from + 1)));
                cursor = to;
            }

            if (cursor < pts.Count - 1) pieces.Add(new Piece(si, cursor, thread, false, pts.GetRange(cursor, pts.Count - cursor)));
        }

        // 2. Satin columns and their covered area.
        var objects = new List<(Piece Piece, EmbroideryObject Object)>();
        var covers = new List<(Bounds Box, Region Area)>();
        foreach (var piece in pieces.Where(p => p.Satin))
        {
            var column = Column(piece.Points, options.PullCompensationMm);
            objects.Add((piece, column with { Name = $"Saten {objects.Count + 1}", ThreadIndex = piece.Thread }));
            var outline = new Region([piece.Points.Where((_, i) => i % 2 == 0).Concat(piece.Points.Where((_, i) => i % 2 == 1).Reverse()).ToArray()], FillRule.NonZero);
            var area = PolygonOps.Offset(outline, 0.3);
            covers.Add((area.Bounds, area));
        }

        // 3. Running pieces: dropped when they are tie-ins or lie under a column.
        var dropped = 0;
        var runs = 0;
        var keptRunStitches = 0;
        foreach (var piece in pieces.Where(p => !p.Satin))
        {
            var length = piece.Points.Zip(piece.Points.Skip(1), Vec2.Distance).Sum();
            var covered = piece.Points.Count(p => covers.Any(c => c.Box.Contains(p) && PolygonOps.Contains(c.Area, p)));
            if (length < 1.0 || covered >= 0.8 * piece.Points.Count)
            {
                dropped += piece.Points.Count;
                continue;
            }

            runs++;
            keptRunStitches += piece.Points.Count;
            objects.Add((piece, new RunObject
            {
                Id = Guid.NewGuid(), Name = $"Çizgi {runs}", ThreadIndex = piece.Thread,
                Path = PolygonOps.SimplifyPath(piece.Points, 0.05),
            }));
        }

        var ordered = objects.OrderBy(o => o.Piece.Sequence).ThenBy(o => o.Piece.Start).Select(o => o.Object).ToList();
        var threadCount = sequences.Count == 0 ? 1 : sequences.Max(s => s.Thread) + 1;
        var design = new Design
        {
            Id = Guid.NewGuid(),
            Name = name,
            Threads = Enumerable.Range(0, threadCount).Select(i => new EmbroideryThread($"İplik {i + 1}", Palette[i % Palette.Length])).ToList(),
            Objects = ordered,
            Hoop = ProjectService.SmallestHoopFor(ordered),
        };

        var satinCount = objects.Count(o => o.Piece.Satin);
        var diagnostics = new List<Diagnostic>
        {
            Diagnostic.Info("TRC001", $"Traced {satinCount} satin column(s) and {runs} run(s) from {total} stitches; {dropped} underlay, travel and tie-in stitches were dropped."),
        };
        if (total > 0 && keptRunStitches > 0.3 * total)
        {
            diagnostics.Add(Diagnostic.Warning("TRC002", $"{keptRunStitches} stitches were kept as run lines; fills are not recognised and should be re-digitized as tatami."));
        }

        return new TraceResult(design, satinCount, runs, dropped, total, diagnostics);
    }

    private static List<(int Thread, List<Vec2> Points)> Sequences(EncodedStitchPlan plan, double unitsPerMm)
    {
        var result = new List<(int, List<Vec2>)>();
        List<Vec2>? current = null;
        var thread = 0;
        foreach (var st in plan.Stitches)
        {
            switch (st.Command)
            {
                case EncodedCommand.Stitch:
                {
                    // Machine units are Y up; the design is Y down.
                    var p = new Vec2(st.X / unitsPerMm, -st.Y / unitsPerMm);
                    if (current is null)
                    {
                        current = [];
                        result.Add((thread, current));
                    }

                    if (current.Count == 0 || Vec2.Distance(current[^1], p) > 1e-9) current.Add(p);
                    break;
                }

                case EncodedCommand.ColorChange:
                    current = null;
                    thread++;
                    break;
                default:
                    current = null;
                    break;
            }
        }

        return result.Where(s => s.Item2.Count >= 2).ToList();
    }

    /// <summary>Penetration index ranges [from, to] sewn as zig-zag satin throws.</summary>
    private static List<(int From, int To)> SatinRanges(List<Vec2> pts, TraceOptions options)
    {
        var ranges = new List<(int, int)>();
        var k = 1;
        while (k < pts.Count - 1)
        {
            if (!Reverses(pts, k, options.MinThrowMm))
            {
                k++;
                continue;
            }

            // Moves k-1 and k reverse: extend while every next move reverses too.
            var start = k - 1;
            var end = k + 1;
            while (end < pts.Count - 1 && Reverses(pts, end, options.MinThrowMm)) end++;
            // The first and last penetrations are often the step from underlay or a tip: keep only
            // throws of a normal length.
            var lengths = Enumerable.Range(start, end - start).Select(i => Vec2.Distance(pts[i], pts[i + 1])).OrderBy(x => x).ToList();
            var typical = lengths[lengths.Count / 2];
            while (start < end && Vec2.Distance(pts[start], pts[start + 1]) < 0.6 * typical) start++;
            while (end > start && Vec2.Distance(pts[end - 1], pts[end]) < 0.6 * typical) end--;
            var throws = end - start;
            if (throws >= options.MinSatinThrows && SameRailSpacing(pts, start, end) <= options.MaxSatinSpacingMm)
            {
                ranges.Add((start, end));
            }

            k = end + 1;
        }

        return ranges;
    }

    private static bool Reverses(List<Vec2> pts, int k, double minThrow)
    {
        var a = pts[k] - pts[k - 1];
        var b = pts[k + 1] - pts[k];
        if (a.Length < minThrow || b.Length < minThrow) return false;
        return Vec2.Dot(a, b) / (a.Length * b.Length) < -0.5;
    }

    private static double SameRailSpacing(List<Vec2> pts, int from, int to)
    {
        var d = new List<double>();
        for (var i = from; i + 2 <= to; i++) d.Add(Vec2.Distance(pts[i], pts[i + 2]));
        d.Sort();
        return d.Count == 0 ? double.MaxValue : d[d.Count / 2];
    }

    private static SatinObject Column(List<Vec2> pts, double pull)
    {
        // Undo pull compensation: move every penetration towards its throw partner.
        var q = new Vec2[pts.Count];
        for (var i = 0; i < pts.Count; i++)
        {
            var partner = i + 1 < pts.Count ? pts[i + 1] : pts[i - 1];
            var d = partner - pts[i];
            var shift = Math.Min(pull / 2, Math.Max(0, d.Length / 2 - 0.1));
            q[i] = d.Length < 1e-9 ? pts[i] : pts[i] + d.Normalized() * shift;
        }

        // Short stitches (inner side of curves) end inside the column, not on the rail: a
        // penetration set in from the line of its same-side neighbours towards the other rail.
        var onRail = new bool[q.Length];
        for (var i = 0; i < q.Length; i++)
        {
            onRail[i] = true;
            if (i < 2 || i + 2 >= q.Length) continue;
            var (p0, p1) = (q[i - 2], q[i + 2]);
            var chord = p1 - p0;
            if (chord.Length < 1e-9) continue;
            var offset = Vec2.Cross(chord, q[i] - p0) / chord.Length;
            var towardsOther = Vec2.Cross(chord, q[i + 1] - p0) / chord.Length;
            if (Math.Abs(offset) > 0.25 && Math.Sign(offset) == Math.Sign(towardsOther)) onRail[i] = false;
        }

        var a = q.Where((_, i) => i % 2 == 0 && onRail[i]).ToArray();
        var b = q.Where((_, i) => i % 2 == 1 && onRail[i]).ToArray();
        var fullPairs = Enumerable.Range(0, q.Length / 2).Where(j => onRail[2 * j] && onRail[2 * j + 1]).Select(j => (A: q[2 * j], B: q[2 * j + 1])).ToList();

        // Rungs about every 3 mm keep the rails in step through curves; a short column needs none
        // (and its rungs could be longer than its rails, which the SVG convention cannot tell apart).
        var rungs = new List<Rung>();
        var railLength = a.Zip(a.Skip(1), Vec2.Distance).Sum();
        var widest = fullPairs.Count == 0 ? 0 : fullPairs.Max(p => Vec2.Distance(p.A, p.B));
        if (railLength > 2 * widest)
        {
            double walked = 0, next = 3;
            for (var j = 1; j < fullPairs.Count - 1; j++)
            {
                walked += Vec2.Distance(fullPairs[j - 1].A, fullPairs[j].A);
                if (walked < next) continue;
                var rung = new Rung(fullPairs[j].A, fullPairs[j].B);
                if (rungs.Count > 0 && Crosses(rungs[^1], rung)) continue; // throws at a sharp turn fan out
                rungs.Add(rung);
                next = walked + 3;
            }
        }

        return new SatinObject
        {
            Id = Guid.NewGuid(),
            Name = "",
            ThreadIndex = 0,
            Source = SatinSource.Rails,
            RailA = PolygonOps.SimplifyPath(a, 0.05),
            RailB = PolygonOps.SimplifyPath(b, 0.05),
            Rungs = rungs,
        };
    }

    private static bool Crosses(Rung p, Rung q)
    {
        static double Side(Vec2 a, Vec2 b, Vec2 c) => Vec2.Cross(b - a, c - a);
        return Side(p.A, p.B, q.A) * Side(p.A, p.B, q.B) < 0 && Side(q.A, q.B, p.A) * Side(q.A, q.B, p.B) < 0;
    }
}
