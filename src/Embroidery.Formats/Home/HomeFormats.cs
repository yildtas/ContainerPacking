using System.Globalization;
using System.Text;
using Embroidery.Machine;

namespace Embroidery.Formats.Home;

/// <summary>
/// Writers for home-machine formats, ported from pyembroidery 1.5.1 (MIT licence). All take a plan
/// encoded with the matching <see cref="MachineProfile"/> (0.1 mm units, Y up, moves of at most
/// ±121 units) and the thread colour of each colour block (#RRGGBB), in sewing order.
/// </summary>
public static class HomeFormats
{
    // ---------------------------------------------------------------- Melco EXP

    /// <summary>Melco/Bernina EXP: two signed bytes per stitch, 0x80-prefixed commands.</summary>
    public static byte[] WriteExp(EncodedStitchPlan plan)
    {
        using var ms = new MemoryStream();
        int x = 0, y = 0;
        foreach (var s in plan.Stitches)
        {
            int dx = s.X - x, dy = s.Y - y;
            switch (s.Command)
            {
                case EncodedCommand.Stitch:
                    ms.Write([(byte)dx, (byte)dy]);
                    break;
                case EncodedCommand.Jump:
                    ms.Write([0x80, 0x04, (byte)dx, (byte)dy]);
                    break;
                case EncodedCommand.Trim:
                    ms.Write([0x80, 0x80, 0x07, 0x00]);
                    break;
                case EncodedCommand.ColorChange:
                    ms.Write([0x80, 0x01, 0x00, 0x00]);
                    break;
            }

            if (s.Command == EncodedCommand.End) break;
            if (s.Command is EncodedCommand.Stitch or EncodedCommand.Jump) (x, y) = (s.X, s.Y);
        }

        return ms.ToArray();
    }

    // ---------------------------------------------------------------- Janome JEF

    /// <summary>Janome JEF: header with hoop and palette, then two signed bytes per stitch.</summary>
    public static byte[] WriteJef(EncodedStitchPlan plan, IReadOnlyList<string> blockColors, DateTime? date = null)
    {
        var colors = BlockColors(plan, blockColors);
        var palette = new List<int>();
        int? lastIndex = null;
        string? lastColor = null;
        foreach (var c in colors)
        {
            var rgb = Rgb(c);
            var index = Nearest(HomeFormatTables.JefColors, rgb, exclude: null);
            // Two different threads must not collapse into the same chart colour back to back.
            if (index == lastIndex && c != lastColor) index = Nearest(HomeFormatTables.JefColors, rgb, exclude: index);
            palette.Add(index);
            lastIndex = index;
            lastColor = c;
        }

        var pointCount = 1; // the end command
        foreach (var s in plan.Stitches)
        {
            if (s.Command == EncodedCommand.End) break;
            pointCount += s.Command switch
            {
                EncodedCommand.Stitch => 1,
                EncodedCommand.Jump or EncodedCommand.ColorChange => 2,
                _ => 0,
            };
        }

        var (width, height) = Size(plan);
        var halfW = (int)Math.Round(width / 2.0, MidpointRounding.ToEven);
        var halfH = (int)Math.Round(height / 2.0, MidpointRounding.ToEven);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x74 + colors.Count * 8);
        w.Write(0x14);
        w.Write(Encoding.ASCII.GetBytes((date ?? DateTime.Now).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write(colors.Count);
        w.Write(pointCount);
        w.Write(JefHoop(width, height));
        w.Write(halfW);
        w.Write(halfH);
        w.Write(halfW);
        w.Write(halfH);
        foreach (var (hx, hy) in new[] { (550, 550), (250, 250), (700, 1000), (700, 1000) })
        {
            var (ex, ey) = (hx - halfW, hy - halfH);
            var fits = Math.Min(ex, ey) >= 0;
            for (var k = 0; k < 2; k++)
            {
                w.Write(fits ? ex : -1);
                w.Write(fits ? ey : -1);
            }
        }

        foreach (var p in palette) w.Write(p);
        for (var i = 0; i < colors.Count; i++) w.Write(0x0D);

        int x = 0, y = 0;
        foreach (var s in plan.Stitches)
        {
            if (s.Command == EncodedCommand.End) break;
            int dx = s.X - x, dy = s.Y - y;
            switch (s.Command)
            {
                case EncodedCommand.Stitch:
                    w.Write((sbyte)dx);
                    w.Write((sbyte)dy);
                    break;
                case EncodedCommand.ColorChange:
                    w.Write([0x80, 0x01]);
                    w.Write((sbyte)dx);
                    w.Write((sbyte)dy);
                    break;
                case EncodedCommand.Jump:
                    w.Write([0x80, 0x02]);
                    w.Write((sbyte)dx);
                    w.Write((sbyte)dy);
                    break;
            }

            if (s.Command is EncodedCommand.Stitch or EncodedCommand.Jump or EncodedCommand.ColorChange) (x, y) = (s.X, s.Y);
        }

        w.Write([0x80, 0x10]);
        w.Flush();
        return ms.ToArray();
    }

    private static int JefHoop(int width, int height) =>
        width < 500 && height < 500 ? 1 // 50 × 50
        : width < 1260 && height < 1100 ? 3 // 126 × 110
        : width < 1400 && height < 2000 ? 2 // 140 × 200
        : width < 2000 && height < 2000 ? 4 // 200 × 200
        : 0; // 110 × 110 (default)

    // ---------------------------------------------------------------- Brother PES / PEC

    /// <summary>
    /// Brother PES version 1 with an empty design section and the PEC block machines sew from
    /// (pyembroidery's "truncated" PES). PE-Design shows it as stitches without objects.
    /// </summary>
    public static byte[] WritePes(EncodedStitchPlan plan, IReadOnlyList<string> blockColors)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("#PES0001"));
        ms.Write([0x16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        WritePec(ms, plan, BlockColors(plan, blockColors));
        return ms.ToArray();
    }

    private static void WritePec(Stream f, EncodedStitchPlan plan, IReadOnlyList<string> colors)
    {
        // Header: label, icon size, palette indices, padding to 512 bytes.
        var label = new string(plan.Label.Where(c => c is >= ' ' and <= '~').Take(8).ToArray());
        f.Write(Encoding.ASCII.GetBytes($"LA:{label,-16}\r"));
        f.Write([0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0xFF, 0x00]);
        f.WriteByte(48 / 8);
        f.WriteByte(38);
        var indices = UniquePalette(colors);
        f.Write([0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20]);
        f.WriteByte((byte)(indices.Count - 1));
        foreach (var i in indices) f.WriteByte((byte)i);
        for (var i = indices.Count; i < 463; i++) f.WriteByte(0x20);

        // Stitch block.
        var (width, height) = Size(plan);
        using var block = new MemoryStream();
        block.Write([0x00, 0x00, 0, 0, 0, 0x31, 0xFF, 0xF0]);
        WriteInt16(block, width);
        WriteInt16(block, height);
        WriteInt16(block, 0x1E0);
        WriteInt16(block, 0x1B0);
        EncodePec(block, plan);
        var bytes = block.ToArray();
        var length = bytes.Length;
        bytes[2] = (byte)length;
        bytes[3] = (byte)(length >> 8);
        bytes[4] = (byte)(length >> 16);
        f.Write(bytes);

        // Thumbnails: the whole design, then one per colour block.
        var bounds = DownBounds(plan);
        var all = (byte[])HomeFormatTables.PecBlankIcon.Clone();
        foreach (var block2 in ColorBlocks(plan)) Draw(all, bounds, block2, buffer: 4);
        f.Write(all);
        foreach (var block2 in ColorBlocks(plan))
        {
            var icon = (byte[])HomeFormatTables.PecBlankIcon.Clone();
            Draw(icon, bounds, block2, buffer: 5);
            f.Write(icon);
        }
    }

    private static void EncodePec(Stream f, EncodedStitchPlan plan)
    {
        const int jumpCode = 0x10;
        const int trimCode = 0x20;
        var colorTwo = true;
        var jumping = true;
        var init = true;
        int x = 0, y = 0;
        foreach (var s in plan.Stitches)
        {
            // PEC is Y down.
            int dx = s.X - x, dy = -(s.Y - y);
            switch (s.Command)
            {
                case EncodedCommand.Stitch:
                    if (jumping)
                    {
                        if (dx != 0 && dy != 0) { Value(f, 0); Value(f, 0); }
                        jumping = false;
                    }

                    Value(f, dx);
                    Value(f, dy);
                    break;
                case EncodedCommand.Jump:
                    // Every jump after the first carries the trim flag (as Brother software does).
                    jumping = true;
                    Value(f, dx, long_: true, flag: init ? jumpCode : trimCode);
                    Value(f, dy, long_: true, flag: init ? jumpCode : trimCode);
                    break;
                case EncodedCommand.ColorChange:
                    if (jumping)
                    {
                        Value(f, 0);
                        Value(f, 0);
                        jumping = false;
                    }

                    f.Write([0xFE, 0xB0, (byte)(colorTwo ? 2 : 1)]);
                    colorTwo = !colorTwo;
                    break;
            }

            if (s.Command == EncodedCommand.End) break;
            if (s.Command is EncodedCommand.Stitch or EncodedCommand.Jump or EncodedCommand.ColorChange) (x, y) = (s.X, s.Y);
            if (s.Command != EncodedCommand.Trim) init = false;
        }

        f.WriteByte(0xFF);
    }

    private static void Value(Stream f, int value, bool long_ = false, int flag = 0)
    {
        if (!long_ && value > -64 && value < 63)
        {
            f.WriteByte((byte)(value & 0x7F));
            return;
        }

        var v = (value & 0x0FFF) | 0x8000 | (flag << 8);
        f.Write([(byte)(v >> 8), (byte)v]);
    }

    private static void WriteInt16(Stream f, int v) => f.Write([(byte)v, (byte)(v >> 8)]);

    /// <summary>Palette indices with each distinct colour reserved to its own chart entry.</summary>
    private static List<int> UniquePalette(IReadOnlyList<string> colors)
    {
        var chart = new int[HomeFormatTables.PecColors.Length];
        Array.Fill(chart, -1);
        var available = (int[])HomeFormatTables.PecColors.Clone();
        foreach (var c in colors.Distinct())
        {
            var index = Nearest(available, Rgb(c), exclude: null);
            if (index < 0) break;
            available[index] = -1;
            chart[index] = Rgb(c);
        }

        return colors.Select(c => Nearest(chart, Rgb(c), exclude: null)).ToList();
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) DownBounds(EncodedStitchPlan plan)
    {
        var pts = plan.Stitches.Where(s => s.Command == EncodedCommand.Stitch).ToList();
        if (pts.Count == 0) return (0, 0, 1, 1);
        return (pts.Min(p => p.X), pts.Min(p => -p.Y), pts.Max(p => p.X), pts.Max(p => -p.Y));
    }

    private static IEnumerable<List<(int X, int Y)>> ColorBlocks(EncodedStitchPlan plan)
    {
        var current = new List<(int, int)>();
        foreach (var s in plan.Stitches)
        {
            if (s.Command == EncodedCommand.ColorChange)
            {
                yield return current;
                current = [];
            }
            else if (s.Command == EncodedCommand.Stitch)
            {
                current.Add((s.X, -s.Y));
            }
        }

        yield return current;
    }

    private static void Draw(byte[] icon, (double MinX, double MinY, double MaxX, double MaxY) b, List<(int X, int Y)> points, int buffer)
    {
        const int stride = 6;
        var gw = stride * 8;
        var gh = icon.Length / stride;
        var w = Math.Max(1, b.MaxX - b.MinX);
        var h = Math.Max(1, b.MaxY - b.MinY);
        var scale = Math.Min((gw - buffer) / w, (gh - buffer) / h);
        var tx = -(b.MaxX + b.MinX) / 2 * scale + gw / 2.0;
        var ty = -(b.MaxY + b.MinY) / 2 * scale + gh / 2.0;
        foreach (var (px, py) in points)
        {
            var ix = (int)Math.Floor(px * scale + tx);
            var iy = (int)Math.Floor(py * scale + ty);
            var index = iy * stride + ix / 8;
            if (ix < 0 || iy < 0 || index >= icon.Length) continue;
            icon[index] |= (byte)(1 << (ix % 8));
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>One colour per colour block; missing entries repeat the last colour.</summary>
    private static List<string> BlockColors(EncodedStitchPlan plan, IReadOnlyList<string> colors)
    {
        var blocks = plan.ColorChangeCount + 1;
        var fallback = colors.Count > 0 ? colors[^1] : "#000000";
        return Enumerable.Range(0, blocks).Select(i => i < colors.Count ? colors[i] : fallback).ToList();
    }

    private static (int Width, int Height) Size(EncodedStitchPlan plan)
    {
        var (px, mx, py, my) = plan.Extents;
        return (px + mx, py + my);
    }

    private static int Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        return h.Length == 6 && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Nearest chart entry by the red-mean colour distance (entries of -1 are skipped).</summary>
    private static int Nearest(int[] chart, int rgb, int? exclude)
    {
        int r1 = (rgb >> 16) & 0xFF, g1 = (rgb >> 8) & 0xFF, b1 = rgb & 0xFF;
        var best = -1;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < chart.Length; i++)
        {
            if (chart[i] < 0 || i == exclude) continue;
            int r2 = (chart[i] >> 16) & 0xFF, g2 = (chart[i] >> 8) & 0xFF, b2 = chart[i] & 0xFF;
            var mean = (int)Math.Round((r1 + r2) / 2.0, MidpointRounding.ToEven);
            int r = r1 - r2, g = g1 - g2, b = b1 - b2;
            long d = (((512 + mean) * r * r) >> 8) + 4 * g * g + (((767 - mean) * b * b) >> 8);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }
}
