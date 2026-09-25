using Embroidery.Core.Primitives;

namespace Embroidery.Geometry;

/// <summary>A skeleton branch: centre-line points (mm) and the distance to the outline at each.</summary>
public sealed record SkeletonBranch(IReadOnlyList<Vec2> Points, IReadOnlyList<double> Radius, bool StartsAtTip, bool EndsAtTip)
{
    public double Length => Points.Zip(Points.Skip(1), Vec2.Distance).Sum();
}

/// <summary>
/// Medial skeleton of a region, used to propose satin columns for filled outlines.
/// Raster approach (distance field + thinning), chosen for robustness; the geometry that
/// matters for stitching (rails) is taken back from the exact vector outline afterwards.
/// Steps: rasterise (scanline) → exact Euclidean distance transform (Felzenszwalb) →
/// Zhang–Suen thinning → graph (tips, junctions) → spur pruning scaled by local width →
/// merge through degree-2 nodes → smoothed branches.
/// </summary>
public static class RegionSkeleton
{
    public static IReadOnlyList<SkeletonBranch> Compute(Region region, double resolutionMm, int maxPixels = 2_000_000)
    {
        region = PolygonOps.Normalize(region);
        var b = region.Bounds;
        if (b.IsEmpty) return [];
        var res = resolutionMm;
        while ((b.Width / res + 4) * (b.Height / res + 4) > maxPixels) res *= 1.25;
        var nx = (int)Math.Ceiling(b.Width / res) + 4;
        var ny = (int)Math.Ceiling(b.Height / res) + 4;
        var ox = b.MinX - 2 * res;
        var oy = b.MinY - 2 * res;
        Vec2 ToMm(int i, int j) => new(ox + (i + 0.5) * res, oy + (j + 0.5) * res);

        // 1. Rasterise by scanline at pixel centres.
        var inside = new bool[nx * ny];
        for (var j = 0; j < ny; j++)
        {
            var y = oy + (j + 0.5) * res;
            foreach (var span in PolygonOps.Scanline(region, y))
            {
                var i0 = Math.Max(0, (int)Math.Ceiling((span.X0 - ox) / res - 0.5));
                var i1 = Math.Min(nx - 1, (int)Math.Floor((span.X1 - ox) / res - 0.5));
                for (var i = i0; i <= i1; i++) inside[j * nx + i] = true;
            }
        }

        // 2. Distance (in mm) from each inside pixel to the nearest outside pixel.
        var dist = DistanceTransform(inside, nx, ny, res);

        // 3. Thin to a one-pixel skeleton.
        var skel = (bool[])inside.Clone();
        Thin(skel, nx, ny);

        // 4-6. Graph, pruning, branches.
        return BuildBranches(skel, dist, nx, ny, ToMm, res);
    }

    private static double[] DistanceTransform(bool[] inside, int nx, int ny, double res)
    {
        const double inf = 1e20;
        var f = new double[nx * ny];
        for (var k = 0; k < f.Length; k++) f[k] = inside[k] ? inf : 0;

        var n = Math.Max(nx, ny);
        var line = new double[n];
        var outLine = new double[n];
        var v = new int[n];
        var z = new double[n + 1];

        for (var i = 0; i < nx; i++)
        {
            for (var j = 0; j < ny; j++) line[j] = f[j * nx + i];
            Edt1D(line, ny, outLine, v, z);
            for (var j = 0; j < ny; j++) f[j * nx + i] = outLine[j];
        }

        for (var j = 0; j < ny; j++)
        {
            Array.Copy(f, j * nx, line, 0, nx);
            Edt1D(line, nx, outLine, v, z);
            Array.Copy(outLine, 0, f, j * nx, nx);
        }

        for (var k = 0; k < f.Length; k++) f[k] = Math.Sqrt(f[k]) * res;
        return f;
    }

    /// <summary>Felzenszwalb–Huttenlocher 1D squared distance transform.</summary>
    private static void Edt1D(double[] f, int n, double[] d, int[] v, double[] z)
    {
        var k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (var q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                if (s <= z[k] && k > 0) k--;
                else break;
            }

            if (s <= z[k])
            {
                // k == 0 and the new parabola dominates everywhere.
                v[0] = q;
                z[0] = double.NegativeInfinity;
                z[1] = double.PositiveInfinity;
                continue;
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (var q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            var dq = q - v[k];
            d[q] = (double)dq * dq + f[v[k]];
        }
    }

    private static readonly (int dx, int dy)[] Ring = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    private static void Thin(bool[] img, int nx, int ny)
    {
        bool P(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && img[j * nx + i];
        var remove = new List<int>();
        bool changed;
        do
        {
            changed = false;
            for (var step = 0; step < 2; step++)
            {
                remove.Clear();
                for (var j = 1; j < ny - 1; j++)
                {
                    for (var i = 1; i < nx - 1; i++)
                    {
                        if (!img[j * nx + i]) continue;
                        bool p2 = P(i, j - 1), p3 = P(i + 1, j - 1), p4 = P(i + 1, j), p5 = P(i + 1, j + 1),
                             p6 = P(i, j + 1), p7 = P(i - 1, j + 1), p8 = P(i - 1, j), p9 = P(i - 1, j - 1);
                        var bCount = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0) + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                        if (bCount < 2 || bCount > 6) continue;
                        var seq = new[] { p2, p3, p4, p5, p6, p7, p8, p9, p2 };
                        var a = 0;
                        for (var t = 0; t < 8; t++) if (!seq[t] && seq[t + 1]) a++;
                        if (a != 1) continue;
                        if (step == 0 ? (p2 && p4 && p6) || (p4 && p6 && p8) : (p2 && p4 && p8) || (p2 && p6 && p8)) continue;
                        remove.Add(j * nx + i);
                    }
                }

                foreach (var k in remove) img[k] = false;
                changed |= remove.Count > 0;
            }
        }
        while (changed);
    }

    private sealed class Edge
    {
        public int A;
        public int B;
        public List<int> Pixels = [];
        public bool Removed;
    }

    private static IReadOnlyList<SkeletonBranch> BuildBranches(bool[] skel, double[] dist, int nx, int ny, Func<int, int, Vec2> toMm, double res)
    {
        bool S(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && skel[j * nx + i];
        int Neighbours(int k)
        {
            int i = k % nx, j = k / nx, c = 0;
            foreach (var (dx, dy) in Ring) if (S(i + dx, j + dy)) c++;
            return c;
        }

        // Crossing number: how many separate skeleton arms touch the pixel.
        int Crossings(int k)
        {
            int i = k % nx, j = k / nx, c = 0;
            for (var t = 0; t < 8; t++)
            {
                var (ax, ay) = Ring[t];
                var (bx, by) = Ring[(t + 1) % 8];
                if (!S(i + ax, j + ay) && S(i + bx, j + by)) c++;
            }

            return c;
        }

        // Staircase clean-up: a pixel is redundant when its neighbours stay 8-connected to each
        // other without it (topology is preserved). Afterwards line pixels have two neighbours.
        bool cleaned;
        do
        {
            cleaned = false;
            for (var k = 0; k < skel.Length; k++)
            {
                if (!skel[k]) continue;
                int i = k % nx, j = k / nx;
                var around = Ring.Where(r => S(i + r.dx, j + r.dy)).ToList();
                if (around.Count < 2 || !Connected(around)) continue;
                skel[k] = false;
                cleaned = true;
            }
        }
        while (cleaned);

        var pixels = new List<int>();
        for (var k = 0; k < skel.Length; k++) if (skel[k]) pixels.Add(k);

        // Nodes: tips (one neighbour) and junction pixels (three or more arms). Adjacent junction
        // pixels form one node.
        var node = new Dictionary<int, int>();
        var nodeCount = 0;
        var junction = new HashSet<int>(pixels.Where(k => Neighbours(k) >= 3 && Crossings(k) >= 3));
        foreach (var k in junction)
        {
            if (node.ContainsKey(k)) continue;
            var id = nodeCount++;
            var stack = new Stack<int>([k]);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (!node.TryAdd(p, id)) continue;
                foreach (var (dx, dy) in Ring)
                {
                    var q = (p / nx + dy) * nx + p % nx + dx;
                    if (junction.Contains(q) && !node.ContainsKey(q)) stack.Push(q);
                }
            }
        }

        foreach (var k in pixels.Where(k => Neighbours(k) <= 1)) node[k] = nodeCount++;

        var visited = new HashSet<int>();
        var edges = new List<Edge>();
        void Trace(int startNodePixel, int first)
        {
            var edge = new Edge { A = node[startNodePixel] };
            edge.Pixels.Add(startNodePixel);
            var prev = startNodePixel;
            var cur = first;
            while (true)
            {
                edge.Pixels.Add(cur);
                if (node.TryGetValue(cur, out var end) && end != edge.A || node.ContainsKey(cur) && edge.Pixels.Count > 2)
                {
                    edge.B = node[cur];
                    break;
                }

                visited.Add(cur);
                int? next = null;
                // Prefer 4-connected steps so staircases are walked, not skipped.
                foreach (var (dx, dy) in Ring.OrderBy(r => Math.Abs(r.dx) + Math.Abs(r.dy)))
                {
                    var q = (cur / nx + dy) * nx + cur % nx + dx;
                    if (!S(cur % nx + dx, cur / nx + dy) || q == prev || visited.Contains(q)) continue;
                    if (node.TryGetValue(q, out var qn) && qn == edge.A && edge.Pixels.Count < 3) continue;
                    next = q;
                    break;
                }

                if (next is null)
                {
                    edge.B = -1; // dead end inside a staircase; treated as a tip
                    break;
                }

                prev = cur;
                cur = next.Value;
            }

            if (edge.Pixels.Count >= 2) edges.Add(edge);
        }

        foreach (var (pixel, _) in node.ToList())
        {
            foreach (var (dx, dy) in Ring)
            {
                int i = pixel % nx + dx, j = pixel / nx + dy;
                if (!S(i, j)) continue;
                var q = j * nx + i;
                if (visited.Contains(q) || node.TryGetValue(q, out var qn) && qn == node[pixel]) continue;
                if (node.ContainsKey(q) && node[q] != node[pixel])
                {
                    // Two nodes touching directly: a one-step edge, recorded once.
                    if (node[pixel] < node[q]) edges.Add(new Edge { A = node[pixel], B = node[q], Pixels = [pixel, q] });
                    continue;
                }

                Trace(pixel, q);
            }
        }

        // Closed loops without any node (a ring-shaped region).
        foreach (var k in pixels)
        {
            if (visited.Contains(k) || node.ContainsKey(k)) continue;
            node[k] = nodeCount++;
            var loop = new Edge { A = node[k] };
            var cur = k;
            var prev = -1;
            while (true)
            {
                loop.Pixels.Add(cur);
                visited.Add(cur);
                int? next = null;
                foreach (var (dx, dy) in Ring)
                {
                    var q = (cur / nx + dy) * nx + cur % nx + dx;
                    if (S(cur % nx + dx, cur / nx + dy) && q != prev && !visited.Contains(q)) { next = q; break; }
                }

                if (next is null) break;
                prev = cur;
                cur = next.Value;
            }

            loop.B = loop.A;
            if (loop.Pixels.Count > 3) edges.Add(loop);
        }

        double LengthOf(Edge e) => (e.Pixels.Count - 1) * res;
        int Degree(int n) => edges.Count(e => !e.Removed && (e.A == n || e.B == n)) + edges.Count(e => !e.Removed && e.A == n && e.B == n);

        // Spur pruning: short tip edges hanging off a junction, relative to the width there.
        bool pruned;
        do
        {
            pruned = false;
            foreach (var e in edges.Where(e => !e.Removed))
            {
                var tipA = e.A < 0 || Degree(e.A) == 1;
                var tipB = e.B < 0 || Degree(e.B) == 1;
                if (tipA == tipB) continue; // isolated segment or inner edge
                var junctionPixel = tipA ? e.Pixels[^1] : e.Pixels[0];
                var r = dist[junctionPixel];
                if (LengthOf(e) < 1.6 * r + 2 * res && edges.Count(x => !x.Removed) > 1)
                {
                    e.Removed = true;
                    pruned = true;
                }
            }
        }
        while (pruned);

        // Merge through nodes left with exactly two edges.
        bool merged;
        do
        {
            merged = false;
            var live = edges.Where(e => !e.Removed).ToList();
            foreach (var n in live.SelectMany(e => new[] { e.A, e.B }).Where(x => x >= 0).Distinct())
            {
                var inc = live.Where(e => !e.Removed && (e.A == n || e.B == n) && e.A != e.B).ToList();
                if (inc.Count != 2) continue;
                var (e1, e2) = (inc[0], inc[1]);
                var p1 = e1.B == n ? e1.Pixels : Enumerable.Reverse(e1.Pixels).ToList();
                var p2 = e2.A == n ? e2.Pixels : Enumerable.Reverse(e2.Pixels).ToList();
                var joined = new Edge
                {
                    A = e1.B == n ? e1.A : e1.B,
                    B = e2.A == n ? e2.B : e2.A,
                    Pixels = p1.Concat(p2.Skip(1)).ToList(),
                };
                e1.Removed = e2.Removed = true;
                edges.Add(joined);
                merged = true;
                break;
            }
        }
        while (merged);

        var result = new List<SkeletonBranch>();
        foreach (var e in edges.Where(e => !e.Removed))
        {
            var pts = e.Pixels.Select(k => toMm(k % nx, k / nx)).ToList();
            var radius = e.Pixels.Select(k => dist[k]).ToList();
            result.Add(new SkeletonBranch(Smooth(pts), radius, e.A < 0 || Degree(e.A) <= 1, e.B < 0 || Degree(e.B) <= 1));
        }

        return result;
    }

    private static bool Connected(List<(int dx, int dy)> cells)
    {
        var seen = new HashSet<int> { 0 };
        var stack = new Stack<int>([0]);
        while (stack.Count > 0)
        {
            var a = cells[stack.Pop()];
            for (var t = 0; t < cells.Count; t++)
            {
                var c = cells[t];
                if (!seen.Contains(t) && Math.Abs(a.dx - c.dx) <= 1 && Math.Abs(a.dy - c.dy) <= 1 && seen.Add(t)) stack.Push(t);
            }
        }

        return seen.Count == cells.Count;
    }

    /// <summary>Moving average (window 7) keeping the end points; removes the pixel staircase.</summary>
    private static List<Vec2> Smooth(List<Vec2> pts)
    {
        if (pts.Count < 5) return pts;
        var result = new List<Vec2>(pts.Count) { pts[0] };
        for (var i = 1; i < pts.Count - 1; i++)
        {
            var w = Math.Min(3, Math.Min(i, pts.Count - 1 - i));
            var sum = Vec2.Zero;
            for (var k = -w; k <= w; k++) sum += pts[i + k];
            result.Add(sum / (2 * w + 1));
        }

        result.Add(pts[^1]);
        return result;
    }
}
