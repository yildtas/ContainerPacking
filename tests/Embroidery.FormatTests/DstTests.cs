using System.Text;
using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;
using Embroidery.Formats.Dst;
using Embroidery.Machine;

namespace Embroidery.FormatTests;

public class DstRecordTests
{
    [Fact]
    public void Every_delta_in_range_round_trips()
    {
        foreach (var command in new[] { EncodedCommand.Stitch, EncodedCommand.Jump })
        {
            for (var dx = -121; dx <= 121; dx++)
            {
                for (var dy = -121; dy <= 121; dy++)
                {
                    var r = DstFormat.EncodeRecord(dx, dy, command);
                    var (x, y, c) = DstFormat.DecodeRecord(r[0], r[1], r[2]);
                    Assert.True(x == dx && y == dy && c == command, $"({dx},{dy},{command}) decoded as ({x},{y},{c})");
                }
            }
        }
    }

    [Fact]
    public void Special_records_use_standard_codes()
    {
        Assert.Equal(new byte[] { 0, 0, 0xF3 }, DstFormat.EncodeRecord(0, 0, EncodedCommand.End));
        Assert.Equal(new byte[] { 0, 0, 0xC3 }, DstFormat.EncodeRecord(0, 0, EncodedCommand.ColorChange));
        Assert.Equal(new byte[] { 0x01, 0, 0x83 }, DstFormat.EncodeRecord(1, 0, EncodedCommand.Jump));
        Assert.Equal(new byte[] { 0x80, 0, 0x03 }, DstFormat.EncodeRecord(0, 1, EncodedCommand.Stitch));
    }

    [Fact]
    public void Out_of_range_delta_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DstFormat.EncodeRecord(122, 0, EncodedCommand.Stitch));
    }
}

public class DstWriterReaderTests
{
    private static EncodedStitchPlan Sample() => new("TEST", new EncodedStitch[]
    {
        new(10, 10, EncodedCommand.Jump),
        new(50, 10, EncodedCommand.Stitch),
        new(50, -30, EncodedCommand.Stitch),
        new(50, -30, EncodedCommand.ColorChange),
        new(-20, -30, EncodedCommand.Jump),
        new(-20, 5, EncodedCommand.Stitch),
        new(-20, 5, EncodedCommand.End),
    });

    [Fact]
    public void Written_file_reads_back_identically()
    {
        var plan = Sample();
        var bytes = DstWriter.Write(plan);
        Assert.Equal(DstFormat.HeaderSize + 3 * plan.Stitches.Count, bytes.Length);

        var (header, read) = DstReader.Read(bytes);
        Assert.Equal(plan.Stitches, read.Stitches);
        Assert.Equal("TEST", header.Label);
        Assert.Equal(7, header.RecordCount);
        Assert.Equal(1, header.ColorChanges);
        Assert.Equal((50, 20, 10, 30), (header.PlusX, header.MinusX, header.PlusY, header.MinusY));
    }

    [Fact]
    public void Header_has_standard_layout()
    {
        var bytes = DstWriter.Write(Sample());
        var text = Encoding.ASCII.GetString(bytes, 0, 20);
        Assert.Equal("LA:TEST            \r", text);
        Assert.Contains((byte)0x1A, bytes.Take(DstFormat.HeaderSize));
        Assert.Equal((byte)' ', bytes[DstFormat.HeaderSize - 1]);
    }

    [Fact]
    public void Missing_end_record_is_added()
    {
        var plan = new EncodedStitchPlan("x", new EncodedStitch[] { new(5, 5, EncodedCommand.Stitch) });
        var (_, read) = DstReader.Read(DstWriter.Write(plan));
        Assert.Equal(EncodedCommand.End, read.Stitches[^1].Command);
    }

    [Fact]
    public void Truncated_file_is_rejected()
    {
        Assert.Throws<DstFormatException>(() => DstReader.Read(new byte[100]));
    }
}

public class MachineEncoderTests
{
    private static LogicalStitchPlan Plan(params LogicalStitch[] stitches) =>
        new([new LogicalStitchBlock(BlockKind.Object, Guid.Empty, 0, stitches)], []);

    private static LogicalStitch S(double x, double y, StitchCommand c = StitchCommand.Stitch) => new(new Vec2(x, y), c);

    [Fact]
    public void Design_is_centred_and_y_axis_points_up()
    {
        var encoded = MachineEncoder.Encode(Plan(S(0, 0, StitchCommand.Jump), S(10, 0), S(10, 20)), MachineProfile.GenericDst, "t");
        var stitches = encoded.Stitches.Where(s => s.Command != EncodedCommand.End).ToList();
        // Bounds centre (5, 10) maps to the origin; screen-up (smaller Y) becomes positive Y.
        Assert.Equal(new EncodedStitch(-50, 100, EncodedCommand.Jump), stitches[0]);
        Assert.Equal(new EncodedStitch(50, 100, EncodedCommand.Stitch), stitches[1]);
        Assert.Equal(new EncodedStitch(50, -100, EncodedCommand.Stitch), stitches[^1]);
    }

    [Fact]
    public void Long_moves_are_split_within_record_and_stitch_limits()
    {
        var encoded = MachineEncoder.Encode(Plan(S(0, 0, StitchCommand.Jump), S(30, 0), S(30, 40, StitchCommand.Jump)), MachineProfile.GenericDst, "t");
        int x = 0, y = 0;
        foreach (var s in encoded.Stitches)
        {
            var (dx, dy) = (s.X - x, s.Y - y);
            Assert.InRange(Math.Abs(dx), 0, 121);
            Assert.InRange(Math.Abs(dy), 0, 121);
            if (s.Command == EncodedCommand.Stitch) Assert.True(Math.Sqrt(dx * dx + dy * dy) <= 121.0001);
            (x, y) = (s.X, s.Y);
        }

        Assert.True(DstWriter.Write(encoded).Length > 0);
    }

    [Fact]
    public void Absolute_quantisation_does_not_drift()
    {
        // 300 stitches of 0.33 mm: per-delta rounding would drift by ~0.3 units each.
        var stitches = new List<LogicalStitch> { S(0, 0, StitchCommand.Jump) };
        for (var i = 1; i <= 300; i++) stitches.Add(S(i * 0.33, 0));
        var encoded = MachineEncoder.Encode(Plan(stitches.ToArray()), MachineProfile.GenericDst, "t");
        var last = encoded.Stitches.Last(s => s.Command == EncodedCommand.Stitch);
        var first = encoded.Stitches.Last(s => s.Command == EncodedCommand.Jump);
        Assert.Equal((int)Math.Round(300 * 0.33 * 10), last.X - first.X);
    }

    [Fact]
    public void Trim_becomes_zero_sum_jump_sequence()
    {
        var encoded = MachineEncoder.Encode(Plan(S(0, 0, StitchCommand.Jump), S(5, 0), S(5, 0, StitchCommand.Trim), S(20, 0, StitchCommand.Jump)), MachineProfile.GenericDst, "t");
        var list = encoded.Stitches.ToList();
        var lastStitch = list.FindLastIndex(s => s.Command == EncodedCommand.Stitch);
        var trim = list.Skip(lastStitch + 1).Take(3).ToList();
        Assert.All(trim, s => Assert.Equal(EncodedCommand.Jump, s.Command));
        Assert.Equal(list[lastStitch].X, trim[^1].X);
        Assert.Equal(list[lastStitch].Y, trim[^1].Y);
    }

    [Fact]
    public void Tie_in_adds_short_lock_stitches()
    {
        var withTie = MachineEncoder.Encode(Plan(S(0, 0, StitchCommand.Jump), S(0, 0, StitchCommand.TieIn), S(10, 0)), MachineProfile.GenericDst, "t");
        var without = MachineEncoder.Encode(Plan(S(0, 0, StitchCommand.Jump), S(10, 0)), MachineProfile.GenericDst, "t");
        Assert.Equal(without.StitchCount + 4, withTie.StitchCount);
    }

    [Fact]
    public void Color_change_is_encoded_and_plan_ends_once()
    {
        var encoded = MachineEncoder.Encode(Plan(S(0, 0, StitchCommand.Jump), S(5, 0), S(5, 0, StitchCommand.ColorChange), S(9, 0), S(9, 0, StitchCommand.End)), MachineProfile.GenericDst, "t");
        Assert.Equal(1, encoded.ColorChangeCount);
        Assert.Single(encoded.Stitches, s => s.Command == EncodedCommand.End);
        Assert.Equal(EncodedCommand.End, encoded.Stitches[^1].Command);
    }
}
