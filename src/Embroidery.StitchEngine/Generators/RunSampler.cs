using Embroidery.Core.Primitives;
using Embroidery.Geometry;

namespace Embroidery.StitchEngine.Generators;

/// <summary>Places evenly spaced penetrations along a polyline, keeping sharp corners.</summary>
public static class RunSampler
{
    /// <summary>
    /// Returns penetrations including the first and last path point. The path is split at
    /// corners sharper than <paramref name="cornerAngleDeg"/>; each piece is divided into equal
    /// stitches no longer than <paramref name="stitchLengthMm"/>, so there is no short leftover.
    /// </summary>
    public static List<Vec2> Sample(IReadOnlyList<Vec2> path, double stitchLengthMm, double cornerAngleDeg = 30)
    {
        var result = new List<Vec2>();
        if (path.Count == 0) return result;
        path = MergeCloseVertices(path, MinVertexSpacingMm);
        result.Add(path[0]);
        if (path.Count == 1) return result;

        stitchLengthMm = Math.Max(0.1, stitchLengthMm);
        var cosLimit = Math.Cos(cornerAngleDeg * Math.PI / 180);
        var piece = new List<Vec2> { path[0] };
        for (var i = 1; i < path.Count; i++)
        {
            piece.Add(path[i]);
            var isLast = i == path.Count - 1;
            if (!isLast)
            {
                var d1 = (path[i] - path[i - 1]).Normalized();
                var d2 = (path[i + 1] - path[i]).Normalized();
                if (Vec2.Dot(d1, d2) >= cosLimit) continue;
            }

            AppendPiece(piece, stitchLengthMm, result);
            piece = [path[i]];
        }

        return result;
    }

    /// <summary>Vertices closer than this would become forced penetrations and produce needle-breaking micro stitches.</summary>
    public const double MinVertexSpacingMm = 0.3;

    /// <summary>Drops vertices too close to the previous kept one; the path end is always kept.</summary>
    private static IReadOnlyList<Vec2> MergeCloseVertices(IReadOnlyList<Vec2> path, double minSpacing)
    {
        if (path.Count < 3) return path;
        var kept = new List<Vec2>(path.Count) { path[0] };
        for (var i = 1; i < path.Count - 1; i++)
        {
            if (Vec2.Distance(kept[^1], path[i]) >= minSpacing) kept.Add(path[i]);
        }

        var end = path[^1];
        if (kept.Count > 1 && Vec2.Distance(kept[^1], end) < minSpacing) kept.RemoveAt(kept.Count - 1);
        kept.Add(end);
        return kept;
    }

    private static void AppendPiece(List<Vec2> piece, double stitchLength, List<Vec2> result)
    {
        var measured = new ArcLengthPath(piece);
        if (measured.Length < 1e-9) return;
        var n = Math.Max(1, (int)Math.Ceiling(measured.Length / stitchLength - 1e-9));
        for (var k = 1; k <= n; k++)
        {
            result.Add(measured.PointAt(measured.Length * k / n));
        }
    }
}
