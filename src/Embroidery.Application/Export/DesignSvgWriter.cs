using System.Globalization;
using System.Security;
using System.Text;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;

namespace Embroidery.Application.Export;

/// <summary>
/// Writes a design as SVG in the vector delivery convention the importer reads
/// (docs/ARCHITECTURE.md §9): <c>data-stitch</c> on every element, satin rails as the first two
/// subpaths followed by two-point rungs, widths and tapers in mm. Importing the file again gives
/// the same objects (sewing parameters other than width/taper are not carried).
/// </summary>
public static class DesignSvgWriter
{
    private const double Pad = 2;

    public static string Write(Design design)
    {
        var bounds = design.Objects.Aggregate(Bounds.Empty, (b, o) => b.Include(o.Bounds));
        if (bounds.IsEmpty) bounds = new Bounds(0, 0, 10, 10);
        var offset = new Vec2(Pad - bounds.MinX, Pad - bounds.MinY);
        var w = bounds.Width + 2 * Pad;
        var h = bounds.Height + 2 * Pad;

        var sb = new StringBuilder();
        sb.Append(Inv($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w:0.###}mm\" height=\"{h:0.###}mm\" viewBox=\"0 0 {w:0.###} {h:0.###}\">\n"));
        sb.Append($"  <title>{Escape(design.Name)}</title>\n");
        foreach (var item in design.Objects)
        {
            var color = item.ThreadIndex >= 0 && item.ThreadIndex < design.Threads.Count ? design.Threads[item.ThreadIndex].ColorHex : "#000000";
            var id = Escape(item.Name);
            switch (item)
            {
                case RunObject r:
                    sb.Append($"  <path id=\"{id}\" data-stitch=\"run\" d=\"{Path(r.Path, offset)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"0.3\"/>\n");
                    break;
                case SatinObject { Source: SatinSource.Stroke } s:
                    sb.Append(Inv($"  <path id=\"{id}\" data-stitch=\"satin\" data-width=\"{s.WidthMm:0.###}\" data-taper-start=\"{s.StartTaperMm:0.###}\" data-taper-end=\"{s.EndTaperMm:0.###}\" d=\"{Path(s.Centerline, offset)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{s.WidthMm:0.###}\"/>\n"));
                    break;
                case SatinObject s:
                {
                    var d = new StringBuilder(Path(s.RailA, offset)).Append(' ').Append(Path(s.RailB, offset));
                    foreach (var rung in s.Rungs) d.Append(' ').Append(Path([rung.A, rung.B], offset));
                    sb.Append($"  <path id=\"{id}\" data-stitch=\"satin\" d=\"{d}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"0.3\"/>\n");
                    break;
                }

                case TatamiObject t:
                {
                    var d = string.Join(' ', t.Region.Rings.Select(ring => Path(ring, offset) + "Z"));
                    var rule = t.Region.FillRule == FillRule.NonZero ? "nonzero" : "evenodd";
                    sb.Append($"  <path id=\"{id}\" data-stitch=\"fill\" d=\"{d}\" fill=\"{color}\" fill-rule=\"{rule}\" stroke=\"none\"/>\n");
                    break;
                }

                case RopeObject r:
                    sb.Append(Inv($"  <path id=\"{id}\" data-stitch=\"rope\" data-width=\"{r.WidthMm:0.###}\" data-pitch=\"{r.Parameters.PitchMm:0.###}\" d=\"{Path(r.Path, offset)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{r.WidthMm:0.###}\"/>\n"));
                    break;
            }
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static string Path(IReadOnlyList<Vec2> points, Vec2 offset)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i] + offset;
            sb.Append(i == 0 ? 'M' : 'L').Append(Inv($"{p.X:0.###},{p.Y:0.###}"));
        }

        return sb.ToString();
    }

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string s) => SecurityElement.Escape(s) ?? "";
}
