using System.Globalization;
using System.Text;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;

namespace Embroidery.Application.Calibration;

/// <summary>
/// A sew-out test sheet: each object varies one parameter so a single physical test on the
/// target fabric/thread/needle/stabilizer shows which values work. Fits a 130 × 180 mm hoop.
/// The legend maps object names and positions to parameters.
/// </summary>
public static class CalibrationSheet
{
    public sealed record Entry(string Name, string Group, string Parameters, Vec2 Center);

    public static (Design Design, IReadOnlyList<Entry> Legend) Build(string threadColor = "#D4A53C")
    {
        var objects = new List<EmbroideryObject>();
        var legend = new List<Entry>();
        var ci = CultureInfo.InvariantCulture;

        void Add(EmbroideryObject o, string group, string parameters)
        {
            objects.Add(o);
            legend.Add(new Entry(o.Name, group, parameters, o.Bounds.Center));
        }

        SatinObject Column(string name, double x, double y0, double y1, double width, SatinParameters p) => new()
        {
            Id = Guid.NewGuid(), Name = name, ThreadIndex = 0, Source = SatinSource.Stroke,
            Centerline = [new(x, y0), new(x, y1)], WidthMm = width, Parameters = p,
        };

        // A: straight satin, width × spacing.
        double[] widths = [2, 3, 4, 5, 6];
        double[] spacings = [0.30, 0.40];
        for (var row = 0; row < spacings.Length; row++)
        {
            for (var i = 0; i < widths.Length; i++)
            {
                var name = string.Create(ci, $"A{row + 1}.{i + 1}");
                var p = new SatinParameters { SpacingMm = spacings[row] };
                Add(Column(name, 12 + i * 14, 10 + row * 35, 35 + row * 35, widths[i], p), "Düz satin",
                    string.Create(ci, $"genişlik {widths[i]} mm, sıklık {spacings[row]:0.00} mm"));
            }
        }

        // B: pull compensation on a 4 mm column.
        double[] pulls = [0, 0.2, 0.4];
        for (var i = 0; i < pulls.Length; i++)
        {
            var p = new SatinParameters { SpacingMm = 0.35, PullCompensationMm = pulls[i] };
            Add(Column(string.Create(ci, $"B{i + 1}"), 12 + i * 14, 80, 105, 4, p), "Pull compensation",
                string.Create(ci, $"genişlik 4 mm, sıklık 0.35, pull {pulls[i]:0.0} mm"));
        }

        // C: curved satin (half circles, radius 8 mm, 4 mm wide).
        for (var i = 0; i < spacings.Length; i++)
        {
            var cx = 72 + i * 26.0;
            var arc = Enumerable.Range(0, 91).Select(k =>
            {
                var t = Math.PI + Math.PI * k / 90;
                return new Vec2(cx + 8 * Math.Cos(t), 100 + 8 * Math.Sin(t));
            }).ToArray();
            var o = new SatinObject
            {
                Id = Guid.NewGuid(), Name = string.Create(ci, $"C{i + 1}"), ThreadIndex = 0, Source = SatinSource.Stroke,
                Centerline = arc, WidthMm = 4, StartTaperMm = 3, EndTaperMm = 3,
                Parameters = new SatinParameters { SpacingMm = spacings[i] },
            };
            Add(o, "Kavisli satin", string.Create(ci, $"yarıçap 8 mm, genişlik 4 mm, sıklık {spacings[i]:0.00}, uçlar 3 mm incelir"));
        }

        // D: tatami row spacing.
        double[] rows = [0.35, 0.40, 0.45];
        for (var i = 0; i < rows.Length; i++)
        {
            var x0 = 8 + i * 40.0;
            var o = new TatamiObject
            {
                Id = Guid.NewGuid(), Name = string.Create(ci, $"D{i + 1}"), ThreadIndex = 0,
                Region = new Region([new Vec2[] { new(x0, 118), new(x0 + 30, 118), new(x0 + 30, 140), new(x0, 140) }]),
                Parameters = new TatamiParameters { RowSpacingMm = rows[i] },
            };
            Add(o, "Tatami", string.Create(ci, $"30×22 mm, satır aralığı {rows[i]:0.00} mm, açı 45°"));
        }

        // E: run stitch length.
        double[] lengths = [2.0, 2.5, 3.0];
        for (var i = 0; i < lengths.Length; i++)
        {
            var y = 152 + i * 8.0;
            var o = new RunObject
            {
                Id = Guid.NewGuid(), Name = string.Create(ci, $"E{i + 1}"), ThreadIndex = 0,
                Path = [new(8, y), new(122, y)], Parameters = new RunParameters { StitchLengthMm = lengths[i] },
            };
            Add(o, "Run", string.Create(ci, $"dikiş boyu {lengths[i]:0.0} mm"));
        }

        var design = new Design
        {
            Id = Guid.NewGuid(),
            Name = "kalibrasyon",
            Revision = 1,
            Threads = [new EmbroideryThread("Test ipliği", threadColor)],
            Objects = objects,
            Hoop = Hoop.Default,
        };
        return (design, legend);
    }

    public static string LegendMarkdown(IReadOnlyList<Entry> legend)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Kalibrasyon test sayfası");
        sb.AppendLine();
        sb.AppendLine("Konumlar tasarımın sol üst köşesine göre mm (X sağa, Y aşağı). Her deneme için kumaş, iplik, iğne, stabilizer, makine hızı ve gerginliği kaydedin.");
        sb.AppendLine();
        sb.AppendLine("| Nesne | Grup | Parametreler | Merkez (mm) | Sonuç / not |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var e in legend)
        {
            sb.AppendLine(string.Create(ci, $"| {e.Name} | {e.Group} | {e.Parameters} | {e.Center.X:0}, {e.Center.Y:0} | |"));
        }

        return sb.ToString();
    }
}
