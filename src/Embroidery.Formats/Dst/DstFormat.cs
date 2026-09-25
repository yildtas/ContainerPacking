using System.Globalization;
using System.Text;
using Embroidery.Machine;

namespace Embroidery.Formats.Dst;

/// <summary>
/// Tajima DST: a 512-byte ASCII header followed by 3-byte records. Each record moves by
/// at most ±121 units (0.1 mm) per axis, encoded in balanced ternary (±1, ±3, ±9, ±27, ±81).
/// The format has no trim and no colours; both are handled by the machine encoder / project.
/// </summary>
public static class DstFormat
{
    public const int HeaderSize = 512;
    public const int MaxDelta = 121;

    public static byte[] EncodeRecord(int dx, int dy, EncodedCommand command)
    {
        if (Math.Abs(dx) > MaxDelta || Math.Abs(dy) > MaxDelta)
        {
            throw new ArgumentOutOfRangeException(nameof(dx), $"DST record delta ({dx}, {dy}) exceeds ±{MaxDelta}.");
        }

        byte b0 = 0, b1 = 0, b2 = 0x03;
        int x = dx, y = dy;

        if (x > 40) { b2 |= 0x04; x -= 81; }
        if (x < -40) { b2 |= 0x08; x += 81; }
        if (y > 40) { b2 |= 0x20; y -= 81; }
        if (y < -40) { b2 |= 0x10; y += 81; }
        if (x > 13) { b1 |= 0x04; x -= 27; }
        if (x < -13) { b1 |= 0x08; x += 27; }
        if (y > 13) { b1 |= 0x20; y -= 27; }
        if (y < -13) { b1 |= 0x10; y += 27; }
        if (x > 4) { b0 |= 0x04; x -= 9; }
        if (x < -4) { b0 |= 0x08; x += 9; }
        if (y > 4) { b0 |= 0x20; y -= 9; }
        if (y < -4) { b0 |= 0x10; y += 9; }
        if (x > 1) { b1 |= 0x01; x -= 3; }
        if (x < -1) { b1 |= 0x02; x += 3; }
        if (y > 1) { b1 |= 0x80; y -= 3; }
        if (y < -1) { b1 |= 0x40; y += 3; }
        if (x > 0) { b0 |= 0x01; x -= 1; }
        if (x < 0) { b0 |= 0x02; x += 1; }
        if (y > 0) { b0 |= 0x80; y -= 1; }
        if (y < 0) { b0 |= 0x40; y += 1; }

        b2 |= command switch
        {
            EncodedCommand.Jump => 0x80,
            EncodedCommand.ColorChange => 0xC0,
            EncodedCommand.End => 0xF0,
            _ => 0x00,
        };

        return [b0, b1, b2];
    }

    public static (int Dx, int Dy, EncodedCommand Command) DecodeRecord(byte b0, byte b1, byte b2)
    {
        var x = 0;
        var y = 0;
        if ((b0 & 0x01) != 0) x += 1;
        if ((b0 & 0x02) != 0) x -= 1;
        if ((b0 & 0x04) != 0) x += 9;
        if ((b0 & 0x08) != 0) x -= 9;
        if ((b0 & 0x80) != 0) y += 1;
        if ((b0 & 0x40) != 0) y -= 1;
        if ((b0 & 0x20) != 0) y += 9;
        if ((b0 & 0x10) != 0) y -= 9;
        if ((b1 & 0x01) != 0) x += 3;
        if ((b1 & 0x02) != 0) x -= 3;
        if ((b1 & 0x04) != 0) x += 27;
        if ((b1 & 0x08) != 0) x -= 27;
        if ((b1 & 0x80) != 0) y += 3;
        if ((b1 & 0x40) != 0) y -= 3;
        if ((b1 & 0x20) != 0) y += 27;
        if ((b1 & 0x10) != 0) y -= 27;
        if ((b2 & 0x04) != 0) x += 81;
        if ((b2 & 0x08) != 0) x -= 81;
        if ((b2 & 0x20) != 0) y += 81;
        if ((b2 & 0x10) != 0) y -= 81;

        var command = (b2 & 0xF3) == 0xF3 && x == 0 && y == 0 && b0 == 0 && b1 == 0
            ? EncodedCommand.End
            : (b2 & 0xC0) == 0xC0
                ? EncodedCommand.ColorChange
                : (b2 & 0x80) != 0
                    ? EncodedCommand.Jump
                    : EncodedCommand.Stitch;
        return (x, y, command);
    }
}

public static class DstWriter
{
    public static void Write(EncodedStitchPlan plan, Stream output)
    {
        var records = new List<byte[]>(plan.Stitches.Count + 1);
        int x = 0, y = 0;
        var ended = false;
        foreach (var s in plan.Stitches)
        {
            if (s.Command == EncodedCommand.Trim)
            {
                // DST has no trim: the Tajima convention of three zero-sum jumps.
                records.Add(DstFormat.EncodeRecord(2, 2, EncodedCommand.Jump));
                records.Add(DstFormat.EncodeRecord(-4, -4, EncodedCommand.Jump));
                records.Add(DstFormat.EncodeRecord(2, 2, EncodedCommand.Jump));
                continue;
            }

            records.Add(DstFormat.EncodeRecord(s.X - x, s.Y - y, s.Command));
            x = s.X;
            y = s.Y;
            if (s.Command == EncodedCommand.End)
            {
                ended = true;
                break;
            }
        }

        if (!ended) records.Add(DstFormat.EncodeRecord(0, 0, EncodedCommand.End));

        output.Write(BuildHeader(plan, records.Count, x, y));
        foreach (var r in records) output.Write(r);
    }

    public static byte[] Write(EncodedStitchPlan plan)
    {
        using var ms = new MemoryStream();
        Write(plan, ms);
        return ms.ToArray();
    }

    private static byte[] BuildHeader(EncodedStitchPlan plan, int recordCount, int endX, int endY)
    {
        var (px, mx, py, my) = plan.Extents;
        var label = new string(plan.Label.Where(c => c is >= ' ' and <= '~').Take(16).ToArray());
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(ci, $"LA:{label,-16}\r");
        sb.Append(ci, $"ST:{recordCount,7}\r");
        sb.Append(ci, $"CO:{plan.ColorChangeCount,3}\r");
        sb.Append(ci, $"+X:{px,5}\r");
        sb.Append(ci, $"-X:{mx,5}\r");
        sb.Append(ci, $"+Y:{py,5}\r");
        sb.Append(ci, $"-Y:{my,5}\r");
        sb.Append(ci, $"AX:{(endX < 0 ? '-' : '+')}{Math.Abs(endX),5}\r");
        sb.Append(ci, $"AY:{(endY < 0 ? '-' : '+')}{Math.Abs(endY),5}\r");
        sb.Append("MX:+    0\r");
        sb.Append("MY:+    0\r");
        sb.Append("PD:******\r");

        var header = new byte[DstFormat.HeaderSize];
        Array.Fill(header, (byte)' ');
        var text = Encoding.ASCII.GetBytes(sb.ToString());
        Array.Copy(text, header, text.Length);
        header[text.Length] = 0x1A;
        return header;
    }
}

public sealed record DstHeader(string Label, int RecordCount, int ColorChanges, int PlusX, int MinusX, int PlusY, int MinusY);

public sealed class DstFormatException(string message) : FormatException(message);

public static class DstReader
{
    public static (DstHeader Header, EncodedStitchPlan Plan) Read(Stream input)
    {
        var header = new byte[DstFormat.HeaderSize];
        if (input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            throw new DstFormatException("File is shorter than the 512-byte DST header.");
        }

        var fields = ParseHeader(header);
        var stitches = new List<EncodedStitch>();
        int x = 0, y = 0;
        var record = new byte[3];
        while (input.ReadAtLeast(record, 3, throwOnEndOfStream: false) == 3)
        {
            var (dx, dy, command) = DstFormat.DecodeRecord(record[0], record[1], record[2]);
            x += dx;
            y += dy;
            stitches.Add(new EncodedStitch(x, y, command));
            if (command == EncodedCommand.End) break;
        }

        int Field(string key) => fields.TryGetValue(key, out var v) && int.TryParse(v.Replace(" ", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var label = fields.TryGetValue("LA", out var la) ? la.Trim() : "";
        var parsed = new DstHeader(label, Field("ST"), Field("CO"), Field("+X"), Field("-X"), Field("+Y"), Field("-Y"));
        return (parsed, new EncodedStitchPlan(label, stitches));
    }

    public static (DstHeader Header, EncodedStitchPlan Plan) Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Read(ms);
    }

    private static Dictionary<string, string> ParseHeader(byte[] header)
    {
        var end = Array.IndexOf(header, (byte)0x1A);
        var text = Encoding.ASCII.GetString(header, 0, end < 0 ? header.Length : end);
        var fields = new Dictionary<string, string>();
        foreach (var line in text.Split('\r', '\n'))
        {
            if (line.Length > 3 && line[2] == ':') fields[line[..2]] = line[3..];
        }

        return fields;
    }
}
