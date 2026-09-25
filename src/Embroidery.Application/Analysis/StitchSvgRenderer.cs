using System.Globalization;
using System.Text;
using Embroidery.Machine;

namespace Embroidery.Application.Analysis;

/// <summary>
/// Renders a machine stream as a standalone SVG preview (no image library needed). Coordinates
/// are converted back to millimetres with Y down. Jumps are drawn as thin dashed lines.
/// </summary>
public static class StitchSvgRenderer
{
    public static string Render(EncodedStitchPlan plan, IReadOnlyList<string> threadColors, string background = "#f4f1ea",
        bool showJumps = true, double unitsPerMm = 10, double threadWidthMm = 0.35)
    {
        var s = plan.Stitches;
        if (s.Count == 0) return "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";
        int minX = s.Min(p => p.X), maxX = s.Max(p => p.X), minY = s.Min(p => p.Y), maxY = s.Max(p => p.Y);
        double pad = 5;
        double w = (maxX - minX) / unitsPerMm + 2 * pad;
        double h = (maxY - minY) / unitsPerMm + 2 * pad;
        string X(int x) => ((x - minX) / unitsPerMm + pad).ToString("0.##", CultureInfo.InvariantCulture);
        string Y(int y) => ((maxY - y) / unitsPerMm + pad).ToString("0.##", CultureInfo.InvariantCulture);
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(w)}mm\" height=\"{F(h)}mm\" viewBox=\"0 0 {F(w)} {F(h)}\">");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"{background}\"/>");

        var color = 0;
        var path = new StringBuilder();
        var jumps = new StringBuilder();
        int px = 0, py = 0;
        var penDown = false;

        void Flush()
        {
            if (path.Length == 0) return;
            var c = threadColors.Count == 0 ? "#222222" : threadColors[color % threadColors.Count];
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{c}\" stroke-width=\"{F(threadWidthMm)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            path.Clear();
        }

        foreach (var st in s)
        {
            switch (st.Command)
            {
                case EncodedCommand.Stitch:
                    path.Append(penDown ? $"L{X(st.X)} {Y(st.Y)}" : $"M{X(px)} {Y(py)}L{X(st.X)} {Y(st.Y)}");
                    penDown = true;
                    break;
                case EncodedCommand.Jump:
                    if (showJumps) jumps.Append($"M{X(px)} {Y(py)}L{X(st.X)} {Y(st.Y)}");
                    penDown = false;
                    break;
                case EncodedCommand.ColorChange:
                    Flush();
                    color++;
                    penDown = false;
                    break;
            }

            px = st.X;
            py = st.Y;
        }

        Flush();
        if (jumps.Length > 0)
        {
            sb.Append($"<path d=\"{jumps}\" fill=\"none\" stroke=\"#3aa0ff\" stroke-width=\"0.15\" stroke-dasharray=\"0.8 0.8\"/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
