using Embroidery.Core.Primitives;

namespace Embroidery.StitchEngine.Sequencing;

/// <summary>
/// Finds a needle-down path between two points that stays under objects sewn later
/// (<see cref="ObjectCoverage"/>), going around uncovered areas when the straight line does
/// not qualify. Grid A* (8-neighbour) followed by string pulling; the route is rejected when it
/// would be much longer than the direct distance, because a long travel costs more than a trim.
/// </summary>
public static class HiddenRouter
{
    private const double EndSlackMm = 1.0;

    public static IReadOnlyList<Vec2>? Route(IReadOnlyList<ObjectCoverage> coverages, Vec2 from, Vec2 to,
        double maxDetourFactor = 3.0, double maxExtraMm = 40, double marginMm = 15, int maxCells = 160, int maxExpansions = 4000)
    {
        if (coverages.Count == 0) return null;
        if (ObjectCoverage.Hides(coverages, from, to, endSlackMm: EndSlackMm)) return [from, to];

        var direct = Vec2.Distance(from, to);
        var bounds = Bounds.Of([from, to]);
        var minX = bounds.MinX - marginMm;
        var minY = bounds.MinY - marginMm;
        var width = bounds.Width + 2 * marginMm;
        var height = bounds.Height + 2 * marginMm;
        // Only coverages that reach the search window matter.
        var window = new Bounds(minX, minY, minX + width, minY + height);
        coverages = coverages.Where(c => c.Overlaps(window)).ToList();
        if (coverages.Count == 0) return null;
        var cell = Math.Max(1.0, Math.Max(width, height) / maxCells);
        var nx = (int)Math.Ceiling(width / cell) + 1;
        var ny = (int)Math.Ceiling(height / cell) + 1;

        Vec2 Center(int i, int j) => new(minX + i * cell, minY + j * cell);
        (int I, int J) CellOf(Vec2 p) => ((int)Math.Round((p.X - minX) / cell), (int)Math.Round((p.Y - minY) / cell));

        // 0 = unknown, 1 = passable, 2 = blocked (evaluated lazily; most cells are never touched).
        var state = new byte[nx * ny];
        bool Passable(int i, int j)
        {
            var k = j * nx + i;
            if (state[k] == 0)
            {
                var c = Center(i, j);
                var open = Vec2.Distance(c, from) <= EndSlackMm + cell || Vec2.Distance(c, to) <= EndSlackMm + cell
                    || coverages.Any(cov => cov.Contains(c));
                state[k] = open ? (byte)1 : (byte)2;
            }

            return state[k] == 1;
        }

        // Cheap rejection: a hidden route needs cover right next to both ends.
        bool CoverNear(Vec2 p)
        {
            var r = EndSlackMm + 2 * cell;
            for (var a = 0; a < 16; a++)
            {
                var q = p + new Vec2(Math.Cos(a * Math.PI / 8), Math.Sin(a * Math.PI / 8)) * r;
                if (coverages.Any(c => c.Contains(q))) return true;
            }

            return false;
        }

        if (!CoverNear(from) || !CoverNear(to)) return null;

        var start = CellOf(from);
        var goal = CellOf(to);
        var gScore = new Dictionary<int, double> { [start.J * nx + start.I] = 0 };
        var cameFrom = new Dictionary<int, int>();
        var open = new PriorityQueue<int, double>();
        open.Enqueue(start.J * nx + start.I, direct);
        var limit = Math.Min(direct * maxDetourFactor, direct + maxExtraMm);
        (int di, int dj, double cost)[] steps =
        [
            (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
            (1, 1, Math.Sqrt(2)), (1, -1, Math.Sqrt(2)), (-1, 1, Math.Sqrt(2)), (-1, -1, Math.Sqrt(2)),
        ];

        var found = false;
        var expansions = 0;
        while (open.TryDequeue(out var current, out _))
        {
            // A trim is always available; do not spend unbounded time looking for a detour.
            if (++expansions > maxExpansions) break;
            if (current == goal.J * nx + goal.I)
            {
                found = true;
                break;
            }

            var ci = current % nx;
            var cj = current / nx;
            var g = gScore[current];
            foreach (var (di, dj, cost) in steps)
            {
                int ni = ci + di, nj = cj + dj;
                if (ni < 0 || nj < 0 || ni >= nx || nj >= ny || !Passable(ni, nj)) continue;
                var key = nj * nx + ni;
                var tentative = g + cost * cell;
                if (tentative > limit) continue;
                if (gScore.TryGetValue(key, out var old) && old <= tentative) continue;
                gScore[key] = tentative;
                cameFrom[key] = current;
                open.Enqueue(key, tentative + Vec2.Distance(Center(ni, nj), to));
            }
        }

        if (!found) return null;

        var cells = new List<Vec2>();
        for (var k = goal.J * nx + goal.I; ; k = cameFrom[k])
        {
            cells.Add(Center(k % nx, k / nx));
            if (!cameFrom.ContainsKey(k)) break;
        }

        cells.Reverse();
        cells[0] = from;
        cells[^1] = to;
        var route = StringPull(coverages, cells);
        var length = route.Zip(route.Skip(1), Vec2.Distance).Sum();
        return length <= limit ? route : null;
    }

    /// <summary>
    /// Keeps only the corners needed: from each kept point, walk forward while the straight line
    /// stays under cover and keep the last point that still does. Linear in the path length.
    /// </summary>
    private static List<Vec2> StringPull(IReadOnlyList<ObjectCoverage> coverages, List<Vec2> points)
    {
        var result = new List<Vec2> { points[0] };
        var i = 0;
        while (i < points.Count - 1)
        {
            var next = i + 1;
            var slackStart = i == 0 ? EndSlackMm : 0;
            for (var j = i + 2; j < points.Count; j++)
            {
                // Only the real end points sit on object edges; intermediate corners must be covered.
                var slackEnd = j == points.Count - 1 ? EndSlackMm : 0;
                if (!ObjectCoverage.Hides(coverages, points[i], points[j], 0.4, slackStart, slackEnd)) break;
                next = j;
            }

            result.Add(points[next]);
            i = next;
        }

        return result;
    }
}
