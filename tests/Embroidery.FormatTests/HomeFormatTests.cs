using System.Text;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Formats;
using Embroidery.Formats.Dst;
using Embroidery.Formats.Home;
using Embroidery.Machine;

namespace Embroidery.FormatTests;

/// <summary>
/// The home formats were checked against pyembroidery 1.5.1 (reading our files gives the same
/// stitches as our DST); these tests pin the byte layout with small hand decoders.
/// </summary>
public class HomeFormatTests
{
    private static LogicalStitchPlan Plan()
    {
        var a = new List<LogicalStitch> { new(new Vec2(0, 0), StitchCommand.Jump) };
        for (var i = 0; i <= 20; i++) a.Add(new(new Vec2(i * 2.0, i % 2 * 3.0), StitchCommand.Stitch));
        a.Add(new(new Vec2(40, 3), StitchCommand.Trim));
        a.Add(new(new Vec2(60, 20), StitchCommand.Jump));
        var b = new List<LogicalStitch> { new(new Vec2(60, 20), StitchCommand.ColorChange) };
        for (var i = 0; i <= 10; i++) b.Add(new(new Vec2(60, 20 + i * 2.5), StitchCommand.Stitch));
        return new LogicalStitchPlan(
        [
            new LogicalStitchBlock(BlockKind.Object, Guid.NewGuid(), 0, a),
            new LogicalStitchBlock(BlockKind.Object, Guid.NewGuid(), 1, b),
        ], []);
    }

    private static EncodedStitchPlan Encode(MachineProfile profile) => MachineEncoder.Encode(Plan(), profile, "test");

    private static List<(int X, int Y)> Stitches(EncodedStitchPlan plan) =>
        plan.Stitches.Where(s => s.Command == EncodedCommand.Stitch).Select(s => (s.X, s.Y)).ToList();

    [Fact]
    public void Native_trim_is_a_command_for_home_formats_and_three_jumps_in_dst()
    {
        var home = Encode(MachineProfile.JanomeJef);
        Assert.Single(home.Stitches, s => s.Command == EncodedCommand.Trim);

        var dst = DstReader.Read(DstWriter.Write(home)).Plan;
        Assert.DoesNotContain(dst.Stitches, s => s.Command == EncodedCommand.Trim);
        Assert.Equal(Stitches(Encode(MachineProfile.GenericDst)), Stitches(dst));
    }

    [Fact]
    public void Exp_decodes_to_the_same_stitches()
    {
        var plan = Encode(MachineProfile.MelcoExp);
        var bytes = HomeFormats.WriteExp(plan);
        var (stitches, trims, colors) = DecodePairs(bytes, 0, exp: true);
        Assert.Equal(Stitches(plan), stitches);
        Assert.Equal(1, trims);
        Assert.Equal(1, colors);
    }

    [Fact]
    public void Jef_header_and_body_are_consistent()
    {
        var plan = Encode(MachineProfile.JanomeJef);
        var bytes = HomeFormats.WriteJef(plan, ["#FF0000", "#0000FF"], new DateTime(2026, 1, 2, 3, 4, 5));
        var offset = BitConverter.ToInt32(bytes, 0);
        Assert.Equal(0x74 + 2 * 8, offset);
        Assert.Equal("20260102030405", Encoding.ASCII.GetString(bytes, 8, 14));
        Assert.Equal(2, BitConverter.ToInt32(bytes, 24));
        var (stitches, _, colors) = DecodePairs(bytes, offset, exp: false);
        Assert.Equal(Stitches(plan), stitches);
        Assert.Equal(1, colors);
        Assert.Equal(new byte[] { 0x80, 0x10 }, bytes[^2..]);
        // Palette: red and blue map to different Janome chart entries.
        Assert.NotEqual(BitConverter.ToInt32(bytes, 0x74), BitConverter.ToInt32(bytes, 0x78));
    }

    [Fact]
    public void Pes_has_a_consistent_pec_block_and_thumbnails()
    {
        var plan = Encode(MachineProfile.BrotherPes);
        var bytes = HomeFormats.WritePes(plan, ["#FF0000", "#0000FF"]);
        Assert.Equal("#PES0001", Encoding.ASCII.GetString(bytes, 0, 8));
        var pec = BitConverter.ToInt32(bytes, 8);
        Assert.Equal("LA:test", Encoding.ASCII.GetString(bytes, pec, 7));
        Assert.Equal(1, bytes[pec + 48]); // colour count - 1
        var block = pec + 512;
        var length = bytes[block + 2] | bytes[block + 3] << 8 | bytes[block + 4] << 16;
        Assert.Equal(0xFF, bytes[block + length - 1]); // end of stitches
        Assert.Equal(bytes.Length, block + length + 3 * 228); // whole-design icon + one per colour
    }

    [Fact]
    public void Every_registered_format_writes_a_file()
    {
        foreach (var format in StitchFormats.All)
        {
            var plan = MachineEncoder.Encode(Plan(), format.Profile, "x");
            Assert.NotEmpty(format.Write(plan, ["#D4A53C", "#1F4E9C"]));
            Assert.Same(format, StitchFormats.Find(format.Extension));
        }
    }

    /// <summary>Minimal decoder for EXP and the JEF body (two signed bytes per stitch, 0x80 escapes).</summary>
    private static (List<(int X, int Y)> Stitches, int Trims, int Colors) DecodePairs(byte[] data, int start, bool exp)
    {
        var stitches = new List<(int, int)>();
        int x = 0, y = 0, trims = 0, colors = 0;
        var i = start;
        while (i + 1 < data.Length)
        {
            if (data[i] == 0x80)
            {
                var cmd = data[i + 1];
                if (!exp && cmd == 0x10) break;
                if (exp && cmd == 0x80) { trims++; i += 4; continue; }
                x += (sbyte)data[i + 2];
                y += (sbyte)data[i + 3];
                if (cmd == 0x01) colors++;
                i += 4;
                continue;
            }

            x += (sbyte)data[i];
            y += (sbyte)data[i + 1];
            stitches.Add((x, y));
            i += 2;
        }

        return (stitches, trims, colors);
    }
}
