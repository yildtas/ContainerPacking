using System.Globalization;
using Embroidery.Core.Primitives;

namespace Embroidery.Geometry.Svg;

/// <summary>One flattened subpath. For closed subpaths the first point is not repeated.</summary>
public sealed record FlatSubpath(IReadOnlyList<Vec2> Points, bool Closed);

public sealed class SvgPathFormatException(string message) : FormatException(message);

/// <summary>
/// Parses SVG path data (all commands, absolute and relative) and flattens curves.
/// Control points are transformed before flattening, so the tolerance applies in output units.
/// </summary>
public static class SvgPathParser
{
    public static List<FlatSubpath> Parse(string data, Matrix2D transform, double tolerance = CurveFlattener.DefaultTolerance)
    {
        var result = new List<FlatSubpath>();
        var reader = new Reader(data);
        var current = new List<Vec2>();
        var pos = Vec2.Zero;          // current point, untransformed
        var subpathStart = Vec2.Zero;
        Vec2? lastCubicCtrl = null;   // for S/s reflection
        Vec2? lastQuadCtrl = null;    // for T/t reflection
        char command = '\0';

        void Flush(bool closed)
        {
            if (current.Count >= 2 || (closed && current.Count >= 1))
            {
                if (closed && current.Count > 1 && current[^1].ApproximatelyEquals(current[0], 1e-9))
                {
                    current.RemoveAt(current.Count - 1);
                }

                result.Add(new FlatSubpath(current.ToArray(), closed));
            }

            current = [];
        }

        void LineTo(Vec2 p)
        {
            current.Add(transform.Apply(p));
            pos = p;
        }

        while (true)
        {
            reader.SkipSeparators();
            if (reader.AtEnd) break;

            if (reader.PeekCommand() is { } c)
            {
                reader.Advance();
                command = c;
            }
            else if (command == '\0')
            {
                throw new SvgPathFormatException("Path data must start with a command.");
            }
            else if (command is 'Z' or 'z')
            {
                throw new SvgPathFormatException("Unexpected number after closepath.");
            }

            var rel = char.IsLower(command);
            var basePos = rel ? pos : Vec2.Zero;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    Flush(false);
                    var p = basePos + reader.ReadPoint();
                    pos = p;
                    subpathStart = p;
                    current.Add(transform.Apply(p));
                    // Subsequent pairs are implicit line-to commands.
                    command = rel ? 'l' : 'L';
                    lastCubicCtrl = lastQuadCtrl = null;
                    break;
                }
                case 'L':
                    LineTo(basePos + reader.ReadPoint());
                    lastCubicCtrl = lastQuadCtrl = null;
                    break;
                case 'H':
                    LineTo(new Vec2((rel ? pos.X : 0) + reader.ReadNumber(), pos.Y));
                    lastCubicCtrl = lastQuadCtrl = null;
                    break;
                case 'V':
                    LineTo(new Vec2(pos.X, (rel ? pos.Y : 0) + reader.ReadNumber()));
                    lastCubicCtrl = lastQuadCtrl = null;
                    break;
                case 'C':
                {
                    var c1 = basePos + reader.ReadPoint();
                    var c2 = basePos + reader.ReadPoint();
                    var end = basePos + reader.ReadPoint();
                    Cubic(c1, c2, end);
                    break;
                }
                case 'S':
                {
                    var c1 = lastCubicCtrl is { } lc ? pos * 2 - lc : pos;
                    var c2 = basePos + reader.ReadPoint();
                    var end = basePos + reader.ReadPoint();
                    Cubic(c1, c2, end);
                    break;
                }
                case 'Q':
                {
                    var q = basePos + reader.ReadPoint();
                    var end = basePos + reader.ReadPoint();
                    Quad(q, end);
                    break;
                }
                case 'T':
                {
                    var q = lastQuadCtrl is { } lq ? pos * 2 - lq : pos;
                    var end = basePos + reader.ReadPoint();
                    Quad(q, end);
                    break;
                }
                case 'A':
                {
                    var rx = reader.ReadNumber();
                    var ry = reader.ReadNumber();
                    var rot = reader.ReadNumber();
                    var large = reader.ReadFlag();
                    var sweep = reader.ReadFlag();
                    var end = basePos + reader.ReadPoint();
                    EnsureStarted();
                    CurveFlattener.Arc(pos, rx, ry, rot, large, sweep, end, transform, tolerance, current);
                    pos = end;
                    lastCubicCtrl = lastQuadCtrl = null;
                    break;
                }
                case 'Z':
                    Flush(true);
                    pos = subpathStart;
                    lastCubicCtrl = lastQuadCtrl = null;
                    // A new subpath implicitly starts at the closed subpath's start.
                    current.Add(transform.Apply(pos));
                    break;
                default:
                    throw new SvgPathFormatException($"Unknown path command '{command}'.");
            }
        }

        // A lone point left behind by a trailing Z is not a subpath.
        if (current.Count >= 2) Flush(false);
        return result;

        void EnsureStarted()
        {
            if (current.Count == 0) current.Add(transform.Apply(pos));
        }

        void Cubic(Vec2 c1, Vec2 c2, Vec2 end)
        {
            EnsureStarted();
            CurveFlattener.Cubic(transform.Apply(pos), transform.Apply(c1), transform.Apply(c2), transform.Apply(end), tolerance, current);
            pos = end;
            lastCubicCtrl = c2;
            lastQuadCtrl = null;
        }

        void Quad(Vec2 q, Vec2 end)
        {
            EnsureStarted();
            CurveFlattener.Quadratic(transform.Apply(pos), transform.Apply(q), transform.Apply(end), tolerance, current);
            pos = end;
            lastQuadCtrl = q;
            lastCubicCtrl = null;
        }
    }

    private sealed class Reader(string text)
    {
        private int _i;

        public bool AtEnd => _i >= text.Length;

        public void SkipSeparators()
        {
            while (_i < text.Length && (char.IsWhiteSpace(text[_i]) || text[_i] == ',')) _i++;
        }

        public char? PeekCommand()
        {
            var c = text[_i];
            return "MmLlHhVvCcSsQqTtAaZz".Contains(c) ? c : null;
        }

        public void Advance() => _i++;

        public Vec2 ReadPoint() => new(ReadNumber(), ReadNumber());

        public bool ReadFlag()
        {
            SkipSeparators();
            if (_i >= text.Length || (text[_i] != '0' && text[_i] != '1'))
            {
                throw new SvgPathFormatException($"Expected arc flag at position {_i}.");
            }

            return text[_i++] == '1';
        }

        public double ReadNumber()
        {
            SkipSeparators();
            var start = _i;
            if (_i < text.Length && (text[_i] == '+' || text[_i] == '-')) _i++;
            var digits = false;
            while (_i < text.Length && char.IsAsciiDigit(text[_i])) { _i++; digits = true; }
            if (_i < text.Length && text[_i] == '.')
            {
                _i++;
                while (_i < text.Length && char.IsAsciiDigit(text[_i])) { _i++; digits = true; }
            }

            if (digits && _i < text.Length && (text[_i] == 'e' || text[_i] == 'E'))
            {
                var save = _i;
                _i++;
                if (_i < text.Length && (text[_i] == '+' || text[_i] == '-')) _i++;
                if (_i < text.Length && char.IsAsciiDigit(text[_i]))
                {
                    while (_i < text.Length && char.IsAsciiDigit(text[_i])) _i++;
                }
                else
                {
                    _i = save;
                }
            }

            if (!digits)
            {
                throw new SvgPathFormatException($"Expected number at position {start}.");
            }

            return double.Parse(text.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
