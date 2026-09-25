using Embroidery.Core.Primitives;

namespace Embroidery.Geometry;

/// <summary>
/// A polyline with a cumulative arc-length table, so points can be sampled by distance
/// travelled rather than by vertex index. Sampling is O(log n).
/// </summary>
public sealed class ArcLengthPath
{
    private readonly Vec2[] _points;
    private readonly double[] _cumulative;

    public ArcLengthPath(IReadOnlyList<Vec2> points)
    {
        if (points.Count == 0) throw new ArgumentException("A path needs at least one point.", nameof(points));

        // Drop consecutive duplicates; they add nothing and break direction queries.
        var pts = new List<Vec2>(points.Count) { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            if (!points[i].ApproximatelyEquals(pts[^1], 1e-9)) pts.Add(points[i]);
        }

        _points = pts.ToArray();
        _cumulative = new double[_points.Length];
        for (var i = 1; i < _points.Length; i++)
        {
            _cumulative[i] = _cumulative[i - 1] + Vec2.Distance(_points[i - 1], _points[i]);
        }
    }

    public IReadOnlyList<Vec2> Points => _points;
    public double Length => _cumulative[^1];
    public Vec2 Start => _points[0];
    public Vec2 End => _points[^1];

    /// <summary>Arc length at vertex <paramref name="index"/>.</summary>
    public double LengthAt(int index) => _cumulative[index];

    /// <summary>Point at distance <paramref name="s"/> from the start (clamped to the path).</summary>
    public Vec2 PointAt(double s)
    {
        if (_points.Length == 1 || s <= 0) return _points[0];
        if (s >= Length) return _points[^1];
        var i = SegmentIndex(s);
        var segLen = _cumulative[i + 1] - _cumulative[i];
        var t = segLen <= 0 ? 0 : (s - _cumulative[i]) / segLen;
        return Vec2.Lerp(_points[i], _points[i + 1], t);
    }

    /// <summary>Point at normalised position <paramref name="t"/> in [0, 1].</summary>
    public Vec2 PointAtFraction(double t) => PointAt(t * Length);

    /// <summary>Unit tangent at distance <paramref name="s"/>.</summary>
    public Vec2 TangentAt(double s)
    {
        if (_points.Length == 1) return Vec2.Zero;
        var i = SegmentIndex(Math.Clamp(s, 0, Length));
        return (_points[i + 1] - _points[i]).Normalized();
    }

    private int SegmentIndex(double s)
    {
        var idx = Array.BinarySearch(_cumulative, s);
        if (idx < 0) idx = ~idx - 1;
        return Math.Clamp(idx, 0, _points.Length - 2);
    }

    public ArcLengthPath Reversed()
    {
        var copy = (Vec2[])_points.Clone();
        Array.Reverse(copy);
        return new ArcLengthPath(copy);
    }
}
