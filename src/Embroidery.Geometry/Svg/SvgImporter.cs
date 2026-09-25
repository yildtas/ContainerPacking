using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Primitives;

namespace Embroidery.Geometry.Svg;

/// <summary>A drawable SVG element in millimetres, top-left origin.</summary>
public sealed record ImportedShape(
    string? ElementId,
    IReadOnlyList<FlatSubpath> Subpaths,
    string? FillColor,
    string? StrokeColor,
    double StrokeWidthMm,
    FillRule FillRule)
{
    /// <summary>
    /// Digitizing hints from the element: <c>data-*</c> attributes without the prefix
    /// (e.g. "stitch", "taper") and Ink/Stitch attributes (e.g. "satin_column"). Keys are lower case.
    /// </summary>
    public IReadOnlyDictionary<string, string> Hints { get; init; } = new Dictionary<string, string>();
}

public sealed record ImportedArtwork(
    double WidthMm,
    double HeightMm,
    double ScaleMmPerUnit,
    IReadOnlyList<ImportedShape> Shapes,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed record SvgImportOptions
{
    /// <summary>When set, the artwork is scaled uniformly so its width equals this value.</summary>
    public double? TargetWidthMm { get; init; }

    public double Tolerance { get; init; } = CurveFlattener.DefaultTolerance;
}

/// <summary>
/// Imports the supported SVG subset into millimetre geometry. Unsupported features are
/// reported as diagnostics instead of being silently dropped.
/// Diagnostic codes: SVG001 parse error, SVG002 unsupported element, SVG003 unsupported
/// feature, SVG004 invalid path data, SVG005 size assumption.
/// </summary>
public static partial class SvgImporter
{
    public const double MmPerPx = 25.4 / 96.0;

    private static readonly HashSet<string> Ignored = ["defs", "metadata", "title", "desc", "symbol", "namedview", "sodipodi:namedview"];
    private static readonly HashSet<string> Unsupported = ["text", "image", "use", "clipPath", "mask", "pattern", "foreignObject", "linearGradient", "radialGradient", "filter", "marker", "switch"];

    public static ImportedArtwork Import(string svgText, SvgImportOptions? options = null)
    {
        options ??= new SvgImportOptions();
        var diagnostics = new List<Diagnostic>();
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(svgText), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            diagnostics.Add(Diagnostic.Error("SVG001", $"SVG could not be parsed: {ex.Message}"));
            return new ImportedArtwork(0, 0, 0, [], diagnostics);
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "svg")
        {
            diagnostics.Add(Diagnostic.Error("SVG001", "Root element is not <svg>."));
            return new ImportedArtwork(0, 0, 0, [], diagnostics);
        }

        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
        var widthMm = ParseLengthMm(root.Attribute("width")?.Value);
        var heightMm = ParseLengthMm(root.Attribute("height")?.Value);

        double scale;
        Vec2 origin;
        double outW, outH;
        if (viewBox is { } vb)
        {
            if (widthMm is null && heightMm is null)
            {
                diagnostics.Add(Diagnostic.Info("SVG005", "SVG has no physical size; assuming 96 DPI (1 user unit = 1 px)."));
            }

            var sx = widthMm is { } w ? w / vb.Width : heightMm is { } h1 ? h1 / vb.Height : MmPerPx;
            var sy = heightMm is { } h ? h / vb.Height : sx;
            scale = Math.Min(sx, sy); // preserveAspectRatio "meet"
            origin = new Vec2(vb.X, vb.Y);
            outW = vb.Width * scale;
            outH = vb.Height * scale;
        }
        else
        {
            scale = MmPerPx;
            origin = Vec2.Zero;
            outW = widthMm ?? 0;
            outH = heightMm ?? 0;
            if (widthMm is null)
            {
                diagnostics.Add(Diagnostic.Info("SVG005", "SVG has no viewBox or size; assuming 96 DPI."));
            }
        }

        var baseTransform = Matrix2D.Scale(scale, scale).Multiply(Matrix2D.Translate(-origin.X, -origin.Y));
        var shapes = new List<ImportedShape>();
        var reported = new HashSet<string>();
        var rootStyle = Style.Default.Inherit(root);
        foreach (var child in root.Elements())
        {
            Walk(child, baseTransform, rootStyle, options, shapes, diagnostics, reported);
        }

        if (options.TargetWidthMm is { } target && outW > 0)
        {
            var f = target / outW;
            shapes = shapes.Select(s => s with
            {
                Subpaths = s.Subpaths.Select(sp => sp with { Points = sp.Points.Select(p => p * f).ToArray() }).ToArray(),
                StrokeWidthMm = s.StrokeWidthMm * f,
            }).ToList();
            scale *= f;
            outW *= f;
            outH *= f;
        }

        return new ImportedArtwork(outW, outH, scale, shapes, diagnostics);
    }

    private static void Walk(XElement el, Matrix2D parent, Style parentStyle, SvgImportOptions options,
        List<ImportedShape> shapes, List<Diagnostic> diagnostics, HashSet<string> reported)
    {
        var name = el.Name.LocalName;
        if (Ignored.Contains(name) || el.Name.NamespaceName is not ("" or "http://www.w3.org/2000/svg")) return;

        if (name == "style")
        {
            Report(diagnostics, reported, "SVG003", "CSS <style> blocks are not supported; use inline attributes or style=\"...\".");
            return;
        }

        if (Unsupported.Contains(name))
        {
            Report(diagnostics, reported, "SVG002", $"<{name}> is not supported and was skipped. Convert it to paths in your editor.");
            return;
        }

        var style = parentStyle.Inherit(el);
        if (!style.Visible) return;
        if (el.Attribute("clip-path") is not null || el.Attribute("mask") is not null)
        {
            Report(diagnostics, reported, "SVG003", "clip-path and mask are ignored; shapes are imported unclipped.");
        }

        var transform = parent;
        if (el.Attribute("transform")?.Value is { } t)
        {
            transform = parent.Multiply(ParseTransform(t));
        }

        if (name is "g" or "svg" or "a")
        {
            foreach (var child in el.Elements()) Walk(child, transform, style, options, shapes, diagnostics, reported);
            return;
        }

        var pathData = ToPathData(el);
        if (pathData is null)
        {
            Report(diagnostics, reported, "SVG002", $"<{name}> is not a supported shape and was skipped.");
            return;
        }

        List<FlatSubpath> subpaths;
        try
        {
            subpaths = SvgPathParser.Parse(pathData, transform, options.Tolerance);
        }
        catch (FormatException ex)
        {
            diagnostics.Add(Diagnostic.Warning("SVG004", $"Invalid path data in <{name}{IdSuffix(el)}>: {ex.Message}"));
            return;
        }

        if (name is "line" or "polyline")
        {
            // These never fill in practice; treat them as strokes only.
            style = style with { Fill = null };
        }

        if (subpaths.Count == 0 || (style.Fill is null && style.Stroke is null)) return;

        shapes.Add(new ImportedShape(
            el.Attribute("id")?.Value,
            subpaths,
            style.Fill,
            style.Stroke,
            style.StrokeWidth * transform.AverageScale,
            style.FillRule) { Hints = ReadHints(el) });
    }

    private static Dictionary<string, string> ReadHints(XElement el)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in el.Attributes())
        {
            var local = a.Name.LocalName.ToLowerInvariant();
            if (a.Name.NamespaceName.Length == 0 && local.StartsWith("data-", StringComparison.Ordinal)) hints[local[5..]] = a.Value.Trim();
            else if (a.Name.NamespaceName.Contains("inkstitch", StringComparison.OrdinalIgnoreCase)) hints[local] = a.Value.Trim();
        }

        return hints;
    }

    private static string IdSuffix(XElement el) => el.Attribute("id")?.Value is { } id ? $" id=\"{id}\"" : "";

    private static void Report(List<Diagnostic> diagnostics, HashSet<string> reported, string code, string message)
    {
        if (reported.Add(message)) diagnostics.Add(Diagnostic.Warning(code, message));
    }

    private static string? ToPathData(XElement el)
    {
        double N(string attr) => ParseNumber(el.Attribute(attr)?.Value) ?? 0;
        var I = CultureInfo.InvariantCulture;
        switch (el.Name.LocalName)
        {
            case "path":
                return el.Attribute("d")?.Value ?? "";
            case "rect":
            {
                double x = N("x"), y = N("y"), w = N("width"), h = N("height");
                if (w <= 0 || h <= 0) return "";
                var rxAttr = ParseNumber(el.Attribute("rx")?.Value);
                var ryAttr = ParseNumber(el.Attribute("ry")?.Value);
                var rx = Math.Min(rxAttr ?? ryAttr ?? 0, w / 2);
                var ry = Math.Min(ryAttr ?? rxAttr ?? 0, h / 2);
                if (rx <= 0 || ry <= 0)
                {
                    return string.Create(I, $"M{x},{y} H{x + w} V{y + h} H{x} Z");
                }

                return string.Create(I,
                    $"M{x + rx},{y} H{x + w - rx} A{rx},{ry} 0 0 1 {x + w},{y + ry} V{y + h - ry} " +
                    $"A{rx},{ry} 0 0 1 {x + w - rx},{y + h} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + h - ry} " +
                    $"V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z");
            }
            case "circle":
            {
                double cx = N("cx"), cy = N("cy"), r = N("r");
                return r <= 0 ? "" : Ellipse(cx, cy, r, r);
            }
            case "ellipse":
            {
                double cx = N("cx"), cy = N("cy"), rx = N("rx"), ry = N("ry");
                return rx <= 0 || ry <= 0 ? "" : Ellipse(cx, cy, rx, ry);
            }
            case "line":
                return string.Create(I, $"M{N("x1")},{N("y1")} L{N("x2")},{N("y2")}");
            case "polyline":
            case "polygon":
            {
                var nums = NumberList(el.Attribute("points")?.Value ?? "");
                if (nums.Count < 4) return "";
                var sb = new System.Text.StringBuilder();
                for (var i = 0; i + 1 < nums.Count; i += 2)
                {
                    sb.Append(i == 0 ? 'M' : 'L').Append(nums[i].ToString(I)).Append(',').Append(nums[i + 1].ToString(I)).Append(' ');
                }

                if (el.Name.LocalName == "polygon") sb.Append('Z');
                return sb.ToString();
            }
            default:
                return null;
        }

        string Ellipse(double cx, double cy, double rx, double ry) => string.Create(I,
            $"M{cx - rx},{cy} A{rx},{ry} 0 1 0 {cx + rx},{cy} A{rx},{ry} 0 1 0 {cx - rx},{cy} Z");
    }

    private sealed record Style(string? Fill, string? Stroke, double StrokeWidth, FillRule FillRule, bool Visible)
    {
        public static readonly Style Default = new("#000000", null, 1, FillRule.NonZero, true);

        public Style Inherit(XElement el)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "fill", "stroke", "stroke-width", "fill-rule", "display", "visibility", "color" })
            {
                if (el.Attribute(key)?.Value is { } v) props[key] = v.Trim();
            }

            // Inline style wins over presentation attributes.
            if (el.Attribute("style")?.Value is { } css)
            {
                foreach (var decl in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = decl.IndexOf(':');
                    if (idx > 0) props[decl[..idx].Trim()] = decl[(idx + 1)..].Trim();
                }
            }

            var s = this;
            if (props.TryGetValue("fill", out var fill) && fill != "inherit") s = s with { Fill = ParseColor(fill) };
            if (props.TryGetValue("stroke", out var stroke) && stroke != "inherit") s = s with { Stroke = ParseColor(stroke) };
            if (props.TryGetValue("stroke-width", out var sw) && ParseNumber(StripUnit(sw)) is { } w) s = s with { StrokeWidth = w };
            if (props.TryGetValue("fill-rule", out var fr)) s = s with { FillRule = fr == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero };
            if (props.TryGetValue("display", out var d) && d == "none") s = s with { Visible = false };
            if (props.TryGetValue("visibility", out var vis) && vis is "hidden" or "collapse") s = s with { Visible = false };
            return s;
        }
    }

    private static string StripUnit(string v) => v.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? v[..^2] : v;

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000", ["white"] = "#FFFFFF", ["red"] = "#FF0000", ["green"] = "#008000",
        ["blue"] = "#0000FF", ["yellow"] = "#FFFF00", ["orange"] = "#FFA500", ["purple"] = "#800080",
        ["gray"] = "#808080", ["grey"] = "#808080", ["navy"] = "#000080", ["maroon"] = "#800000",
        ["lime"] = "#00FF00", ["aqua"] = "#00FFFF", ["cyan"] = "#00FFFF", ["fuchsia"] = "#FF00FF",
        ["magenta"] = "#FF00FF", ["silver"] = "#C0C0C0", ["teal"] = "#008080", ["olive"] = "#808000",
        ["pink"] = "#FFC0CB", ["brown"] = "#A52A2A", ["gold"] = "#FFD700",
    };

    /// <summary>Returns "#RRGGBB" or null for "none"/unparseable paints (e.g. gradients).</summary>
    public static string? ParseColor(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value == "none" || value == "transparent" || value.StartsWith("url(", StringComparison.Ordinal)) return null;
        if (value == "currentColor") return "#000000";
        if (NamedColors.TryGetValue(value, out var named)) return named;
        if (value[0] == '#')
        {
            var hex = value[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            return hex.Length == 6 && hex.All(char.IsAsciiHexDigit) ? "#" + hex.ToUpperInvariant() : null;
        }

        var m = RgbRegex().Match(value);
        if (m.Success)
        {
            int Channel(string v) => v.EndsWith('%')
                ? (int)Math.Round(double.Parse(v[..^1], CultureInfo.InvariantCulture) * 2.55)
                : (int)Math.Round(double.Parse(v, CultureInfo.InvariantCulture));
            var r = Math.Clamp(Channel(m.Groups[1].Value), 0, 255);
            var g = Math.Clamp(Channel(m.Groups[2].Value), 0, 255);
            var b = Math.Clamp(Channel(m.Groups[3].Value), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        return null;
    }

    public static Matrix2D ParseTransform(string value)
    {
        var result = Matrix2D.Identity;
        foreach (Match m in TransformRegex().Matches(value))
        {
            var args = NumberList(m.Groups[2].Value);
            double Arg(int i, double fallback = 0) => i < args.Count ? args[i] : fallback;
            var t = m.Groups[1].Value switch
            {
                "matrix" when args.Count == 6 => new Matrix2D(args[0], args[1], args[2], args[3], args[4], args[5]),
                "translate" => Matrix2D.Translate(Arg(0), Arg(1)),
                "scale" => Matrix2D.Scale(Arg(0, 1), Arg(1, Arg(0, 1))),
                "rotate" when args.Count >= 3 => Matrix2D.Translate(args[1], args[2])
                    .Multiply(Matrix2D.Rotate(args[0])).Multiply(Matrix2D.Translate(-args[1], -args[2])),
                "rotate" => Matrix2D.Rotate(Arg(0)),
                "skewX" => Matrix2D.SkewX(Arg(0)),
                "skewY" => Matrix2D.SkewY(Arg(0)),
                _ => Matrix2D.Identity,
            };
            result = result.Multiply(t);
        }

        return result;
    }

    /// <summary>Parses an SVG length into millimetres; unitless and px use 96 DPI.</summary>
    public static double? ParseLengthMm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var m = LengthRegex().Match(value.Trim());
        if (!m.Success) return null;
        var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "" or "px" => n * MmPerPx,
            "mm" => n,
            "cm" => n * 10,
            "in" => n * 25.4,
            "pt" => n * 25.4 / 72,
            "pc" => n * 25.4 / 6,
            _ => null, // %, em, ex: no absolute size
        };
    }

    private static (double X, double Y, double Width, double Height)? ParseViewBox(string? value)
    {
        if (value is null) return null;
        var n = NumberList(value);
        return n.Count == 4 && n[2] > 0 && n[3] > 0 ? (n[0], n[1], n[2], n[3]) : null;
    }

    private static double? ParseNumber(string? value) =>
        value is not null && double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static List<double> NumberList(string value) =>
        NumberRegex().Matches(value).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();

    [GeneratedRegex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)")]
    private static partial Regex TransformRegex();

    [GeneratedRegex(@"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*([a-zA-Z%]*)$")]
    private static partial Regex LengthRegex();

    [GeneratedRegex(@"^rgb\(\s*([\d.]+%?)\s*,?\s*([\d.]+%?)\s*,?\s*([\d.]+%?)\s*\)$")]
    private static partial Regex RgbRegex();
}
