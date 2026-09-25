using System.Globalization;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;

namespace Embroidery.Application.Projects;

public sealed record HoopPart(int Number, int Column, int Row, Design Design, Bounds Area);

public sealed record SplitResult(IReadOnlyList<HoopPart> Parts, Hoop Hoop, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Succeeded => Parts.Count > 0;
}

/// <summary>
/// Splits a design that does not fit the hoop into a grid of parts that each do. Objects are
/// assigned whole (by their centre) — they are never cut. Neighbouring parts share two small
/// registration crosses on their common edge, sewn first in both parts with a separate
/// "Hizalama" thread, so the operator can re-hoop and line the next part up with the previous.
/// Codes: HOOP001 an object is larger than the hoop, HOOP002 no grid fits.
/// </summary>
public static class HoopSplitter
{
    public const string AlignmentThreadName = "Hizalama";
    private const double MarkArm = 2.0;

    public static SplitResult Split(Design design, Hoop hoop)
    {
        var items = design.Objects.Where(o => o.Visible).ToList();
        var bounds = items.Aggregate(Bounds.Empty, (b, o) => b.Include(o.Bounds));
        if (bounds.IsEmpty) return new SplitResult([], hoop, [Diagnostic.Error("HOOP002", "The design has no visible objects.")]);

        if (Fits(bounds, hoop.WidthMm, hoop.HeightMm) || Fits(bounds, hoop.HeightMm, hoop.WidthMm))
        {
            return new SplitResult([new HoopPart(1, 0, 0, design, bounds)], hoop, []);
        }

        var tooBig = items.Where(o => !Fits(o.Bounds, hoop.WidthMm, hoop.HeightMm) && !Fits(o.Bounds, hoop.HeightMm, hoop.WidthMm)).ToList();
        if (tooBig.Count > 0)
        {
            return new SplitResult([], hoop, tooBig.Select(o => Diagnostic.Error("HOOP001",
                FormattableString.Invariant($"'{o.Name}' is {o.Bounds.Width:0} × {o.Bounds.Height:0} mm, larger than the {hoop.WidthMm:0} × {hoop.HeightMm:0} mm hoop; split it in the artwork."), o.Id)).ToList());
        }

        List<HoopPart>? best = null;
        Hoop? bestHoop = null;
        foreach (var (w, h) in new[] { (hoop.WidthMm, hoop.HeightMm), (hoop.HeightMm, hoop.WidthMm) })
        {
            foreach (var factor in new[] { 1.0, 0.9, 0.8, 0.7, 0.6, 0.5 })
            {
                var parts = TryGrid(design, items, bounds, w, h, factor);
                if (parts is null) continue;
                if (best is null || parts.Count < best.Count)
                {
                    best = parts;
                    bestHoop = hoop with { WidthMm = w, HeightMm = h };
                }

                break;
            }
        }

        return best is null
            ? new SplitResult([], hoop, [Diagnostic.Error("HOOP002", "No grid of hoopings fits this design; objects overlap cell borders too much.")])
            : new SplitResult(best, bestHoop!, [Diagnostic.Info("HOOP003", $"Design split into {best.Count} hoopings with shared registration marks.")]);
    }

    private static bool Fits(Bounds b, double w, double h) => b.Width <= w + 1e-6 && b.Height <= h + 1e-6;

    private static List<HoopPart>? TryGrid(Design design, List<EmbroideryObject> items, Bounds bounds, double w, double h, double factor)
    {
        const double markAllowance = 2 * MarkArm + 2;
        var pitchX = (w - 2 * markAllowance) * factor;
        var pitchY = (h - 2 * markAllowance) * factor;
        var cols = Math.Max(1, (int)Math.Ceiling(bounds.Width / pitchX - 1e-9));
        var rows = Math.Max(1, (int)Math.Ceiling(bounds.Height / pitchY - 1e-9));

        var cells = new List<EmbroideryObject>[cols, rows];
        for (var c = 0; c < cols; c++) for (var r = 0; r < rows; r++) cells[c, r] = [];
        foreach (var o in items)
        {
            var center = o.Bounds.Center;
            var c = Math.Clamp((int)((center.X - bounds.MinX) / pitchX), 0, cols - 1);
            var r = Math.Clamp((int)((center.Y - bounds.MinY) / pitchY), 0, rows - 1);
            cells[c, r].Add(o);
        }

        Bounds Content(int c, int r) => cells[c, r].Aggregate(Bounds.Empty, (b, o) => b.Include(o.Bounds));

        // Registration crosses on every shared edge between two non-empty neighbours.
        var marks = new Dictionary<(int, int), List<Vec2>>();
        void AddMark(int c, int r, Vec2 p)
        {
            if (!marks.TryGetValue((c, r), out var list)) marks[(c, r)] = list = [];
            list.Add(p);
        }

        for (var c = 0; c < cols; c++)
        {
            for (var r = 0; r < rows; r++)
            {
                if (cells[c, r].Count == 0) continue;
                var here = Content(c, r);
                if (c + 1 < cols && cells[c + 1, r].Count > 0)
                {
                    var there = Content(c + 1, r);
                    var x = bounds.MinX + (c + 1) * pitchX;
                    var y0 = Math.Max(here.MinY, there.MinY);
                    var y1 = Math.Min(here.MaxY, there.MaxY);
                    if (y1 <= y0) (y0, y1) = (Math.Min(here.MinY, there.MinY), Math.Max(here.MaxY, there.MaxY));
                    foreach (var f in new[] { 0.25, 0.75 })
                    {
                        var p = new Vec2(x, y0 + (y1 - y0) * f);
                        AddMark(c, r, p);
                        AddMark(c + 1, r, p);
                    }
                }

                if (r + 1 < rows && cells[c, r + 1].Count > 0)
                {
                    var there = Content(c, r + 1);
                    var y = bounds.MinY + (r + 1) * pitchY;
                    var x0 = Math.Max(here.MinX, there.MinX);
                    var x1 = Math.Min(here.MaxX, there.MaxX);
                    if (x1 <= x0) (x0, x1) = (Math.Min(here.MinX, there.MinX), Math.Max(here.MaxX, there.MaxX));
                    foreach (var f in new[] { 0.25, 0.75 })
                    {
                        var p = new Vec2(x0 + (x1 - x0) * f, y);
                        AddMark(c, r, p);
                        AddMark(c, r + 1, p);
                    }
                }
            }
        }

        var alignmentThread = design.Threads.Count;
        var threads = design.Threads.Append(new EmbroideryThread(AlignmentThreadName, "#3AA0FF")).ToList();
        var parts = new List<HoopPart>();
        var number = 0;
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                if (cells[c, r].Count == 0) continue;
                var markObjects = marks.GetValueOrDefault((c, r), []).Select((p, i) => (EmbroideryObject)new RunObject
                {
                    Id = Guid.NewGuid(),
                    Name = string.Create(CultureInfo.InvariantCulture, $"Hizalama {i + 1}"),
                    ThreadIndex = alignmentThread,
                    Path = [p + new Vec2(-MarkArm, 0), p + new Vec2(MarkArm, 0), p, p + new Vec2(0, -MarkArm), p + new Vec2(0, MarkArm)],
                    Parameters = new RunParameters { StitchLengthMm = 1.0, CornerAngleDeg = 10 },
                }).ToList();

                var objects = markObjects.Concat(cells[c, r]).ToList();
                var area = objects.Aggregate(Bounds.Empty, (b, o) => b.Include(o.Bounds));
                if (!Fits(area, w, h)) return null;
                number++;
                parts.Add(new HoopPart(number, c, r, design with
                {
                    Id = Guid.NewGuid(),
                    Name = string.Create(CultureInfo.InvariantCulture, $"{design.Name}-{number}"),
                    Objects = objects,
                    Threads = markObjects.Count > 0 ? threads : design.Threads,
                    Hoop = design.Hoop with { WidthMm = w, HeightMm = h },
                }, area));
            }
        }

        return parts;
    }
}
