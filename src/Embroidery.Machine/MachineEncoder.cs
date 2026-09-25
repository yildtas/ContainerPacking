using Embroidery.Core.Primitives;
using Embroidery.Core.StitchPlan;

namespace Embroidery.Machine;

/// <summary>
/// Turns a logical plan into moves a specific machine format can express:
/// origin at the design centre, Y up, absolute quantisation (no rounding drift),
/// splitting of long moves, trims as jump sequences, and tie stitches.
/// </summary>
public static class MachineEncoder
{
    public static EncodedStitchPlan Encode(LogicalStitchPlan plan, MachineProfile profile, string label)
    {
        var stitches = plan.AllStitches.ToList();
        var bounds = Bounds.Of(stitches
            .Where(s => s.Command is StitchCommand.Stitch or StitchCommand.Travel or StitchCommand.Jump)
            .Select(s => s.Position));
        var origin = bounds.IsEmpty ? Vec2.Zero : bounds.Center;
        var encoder = new Encoder(profile, origin);

        for (var i = 0; i < stitches.Count; i++)
        {
            var s = stitches[i];
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                case StitchCommand.Travel:
                    encoder.MoveTo(s.Position, EncodedCommand.Stitch);
                    break;
                case StitchCommand.Jump:
                    encoder.MoveTo(s.Position, EncodedCommand.Jump);
                    break;
                case StitchCommand.Trim:
                    encoder.Trim();
                    break;
                case StitchCommand.ColorChange:
                case StitchCommand.Stop:
                    encoder.ColorChange();
                    break;
                case StitchCommand.TieIn:
                    encoder.Tie(s.Position, NextDirection(stitches, i, s.Position));
                    break;
                case StitchCommand.TieOff:
                    encoder.Tie(s.Position, PreviousDirection(stitches, i, s.Position));
                    break;
                case StitchCommand.End:
                    encoder.End();
                    break;
            }
        }

        encoder.End();
        return new EncodedStitchPlan(label, encoder.Output);
    }

    private static Vec2 NextDirection(List<LogicalStitch> stitches, int index, Vec2 at)
    {
        for (var j = index + 1; j < stitches.Count; j++)
        {
            if (stitches[j].Command is StitchCommand.Stitch or StitchCommand.Travel && Vec2.Distance(stitches[j].Position, at) > 0.05)
            {
                return (stitches[j].Position - at).Normalized();
            }
        }

        return new Vec2(1, 0);
    }

    private static Vec2 PreviousDirection(List<LogicalStitch> stitches, int index, Vec2 at)
    {
        for (var j = index - 1; j >= 0; j--)
        {
            if (stitches[j].Command is StitchCommand.Stitch or StitchCommand.Travel && Vec2.Distance(stitches[j].Position, at) > 0.05)
            {
                return (stitches[j].Position - at).Normalized();
            }
        }

        return new Vec2(-1, 0);
    }

    private sealed class Encoder(MachineProfile profile, Vec2 origin)
    {
        private int _x;
        private int _y;
        private bool _ended;

        public List<EncodedStitch> Output { get; } = [];

        /// <summary>Quantise the absolute position first, then take deltas: rounding never accumulates.</summary>
        private (int X, int Y) Quantize(Vec2 mm) => (
            (int)Math.Round((mm.X - origin.X) * profile.UnitsPerMm, MidpointRounding.AwayFromZero),
            (int)Math.Round(-(mm.Y - origin.Y) * profile.UnitsPerMm, MidpointRounding.AwayFromZero));

        public void MoveTo(Vec2 mm, EncodedCommand command)
        {
            if (_ended) return;
            var (tx, ty) = Quantize(mm);
            MoveToUnits(tx, ty, command);
        }

        private void MoveToUnits(int tx, int ty, EncodedCommand command)
        {
            var dx = tx - _x;
            var dy = ty - _y;
            if (dx == 0 && dy == 0) return;

            var maxRecord = Math.Max(1, profile.MaxRecordDelta);
            var n = Math.Max(Ceil(Math.Abs(dx), maxRecord), Ceil(Math.Abs(dy), maxRecord));
            if (command == EncodedCommand.Stitch)
            {
                var maxStitch = Math.Max(1, profile.MaxStitchMm * profile.UnitsPerMm);
                n = Math.Max(n, (int)Math.Ceiling(Math.Sqrt((double)dx * dx + (double)dy * dy) / maxStitch - 1e-9));
            }

            int sx = _x, sy = _y;
            for (var k = 1; k <= n; k++)
            {
                // Intermediate points are interpolated from the fixed start, so they cannot drift.
                var px = sx + (int)Math.Round((double)dx * k / n, MidpointRounding.AwayFromZero);
                var py = sy + (int)Math.Round((double)dy * k / n, MidpointRounding.AwayFromZero);
                Output.Add(new EncodedStitch(px, py, command));
                _x = px;
                _y = py;
            }
        }

        private static int Ceil(int value, int step) => (value + step - 1) / step;

        public void Trim()
        {
            if (_ended) return;
            var count = Math.Max(1, profile.Trim.JumpCount);
            var size = Math.Max(1, profile.Trim.JumpSize);
            int bx = _x, by = _y;
            for (var i = 1; i <= count; i++)
            {
                // Zero-sum pattern: +s, -s, +s, ... always finishing back at the start.
                int ox = 0, oy = 0;
                if (i < count)
                {
                    ox = oy = i % 2 == 1 ? size : -size;
                }

                Output.Add(new EncodedStitch(bx + ox, by + oy, EncodedCommand.Jump));
            }

            _x = bx;
            _y = by;
        }

        public void ColorChange()
        {
            if (_ended) return;
            Output.Add(new EncodedStitch(_x, _y, EncodedCommand.ColorChange));
        }

        /// <summary>Two short back-and-forth stitches locking the thread at <paramref name="at"/>.</summary>
        public void Tie(Vec2 at, Vec2 direction)
        {
            if (_ended) return;
            MoveTo(at, EncodedCommand.Stitch);
            var (ax, ay) = (_x, _y);
            var offset = direction * profile.TieStitchMm;
            var ox = (int)Math.Round(offset.X * profile.UnitsPerMm, MidpointRounding.AwayFromZero);
            var oy = (int)Math.Round(-offset.Y * profile.UnitsPerMm, MidpointRounding.AwayFromZero);
            if (ox == 0 && oy == 0) return;
            for (var i = 0; i < 2; i++)
            {
                MoveToUnits(ax + ox, ay + oy, EncodedCommand.Stitch);
                MoveToUnits(ax, ay, EncodedCommand.Stitch);
            }
        }

        public void End()
        {
            if (_ended) return;
            Output.Add(new EncodedStitch(_x, _y, EncodedCommand.End));
            _ended = true;
        }
    }
}
