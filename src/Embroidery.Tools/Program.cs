using System.Globalization;
using System.Text.Json;
using Embroidery.Application.Analysis;
using Embroidery.Application.Calibration;
using Embroidery.Application.Projects;
using Embroidery.Application.Serialization;
using Embroidery.Core.Diagnostics;
using Embroidery.Formats.Dst;
using Embroidery.Geometry.Svg;
using Embroidery.Machine;

// Command-line tools: batch conversion, golden-master measurement and calibration sheets.
// Usage is printed when arguments are missing.

const string Usage = """
    embroidery convert <in.svg> <out.dst> [--width mm] [--report r.json] [--preview p.svg] [--fabric #RRGGBB]
    embroidery analyze <in.dst> [--report r.json] [--preview p.svg] [--fabric #RRGGBB] [--thread #RRGGBB]
    embroidery compare <reference.dst> <candidate.dst|candidate.svg>
    embroidery calibration <out-dir>
    """;

try
{
    return args.Length == 0 ? Fail(Usage) : args[0] switch
    {
        "convert" when args.Length >= 3 => Convert(args[1], args[2], Options(args, 3)),
        "analyze" when args.Length >= 2 => Analyze(args[1], Options(args, 2)),
        "compare" when args.Length >= 3 => Compare(args[1], args[2]),
        "calibration" when args.Length >= 2 => Calibration(args[1]),
        _ => Fail(Usage),
    };
}
catch (Exception ex) when (ex is IOException or FormatException or DesignValidationException or UnauthorizedAccessException or ArgumentException)
{
    return Fail(ex.Message);
}

static int Convert(string input, string output, Dictionary<string, string> options)
{
    var service = new ProjectService();
    double? width = options.TryGetValue("width", out var w) ? double.Parse(w, CultureInfo.InvariantCulture) : null;
    var imported = service.ImportSvg(Path.GetFileName(input), File.ReadAllText(input), new SvgImportOptions { TargetWidthMm = width });
    var (encoded, plan) = service.Encode(imported.Design);
    File.WriteAllBytes(output, DstWriter.Write(encoded));

    var metrics = StitchMetrics.From(encoded);
    Print(Path.GetFileName(output), metrics);
    foreach (var d in imported.Diagnostics.Concat(plan.Diagnostics).Where(d => d.Severity != Severity.Info))
    {
        Console.WriteLine($"  {d.Severity,-7} {d.Code} {d.Message}");
    }

    WriteExtras(options, encoded, metrics, imported.Design.Threads.Select(t => t.ColorHex).ToList());
    return plan.Diagnostics.Any(d => d.Severity == Severity.Error) ? 2 : 0;
}

static int Analyze(string input, Dictionary<string, string> options)
{
    var (header, plan) = DstReader.Read(File.ReadAllBytes(input));
    var metrics = StitchMetrics.From(plan);
    Console.WriteLine($"{Path.GetFileName(input)}: label '{header.Label}', header ST={header.RecordCount} CO={header.ColorChanges}");
    Print(Path.GetFileName(input), metrics);
    var thread = options.GetValueOrDefault("thread", "#D4A53C");
    WriteExtras(options, plan, metrics, [thread]);
    return 0;
}

static int Compare(string reference, string candidate)
{
    var refMetrics = StitchMetrics.From(DstReader.Read(File.ReadAllBytes(reference)).Plan);
    EncodedStitchPlan candPlan;
    if (candidate.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
    {
        var service = new ProjectService();
        candPlan = service.Encode(service.ImportSvg(Path.GetFileName(candidate), File.ReadAllText(candidate)).Design).Encoded;
    }
    else
    {
        candPlan = DstReader.Read(File.ReadAllBytes(candidate)).Plan;
    }

    var c = StitchMetrics.From(candPlan);
    var r = refMetrics;
    Console.WriteLine($"{"metric",-34}{"reference",14}{"candidate",14}{"diff %",10}");
    void Row(string name, double a, double b) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name,-34}{a,14:0.###}{b,14:0.###}{(a == 0 ? double.NaN : (b - a) / a * 100),10:0.0}"));
    Row("stitches", r.Stitches, c.Stitches);
    Row("jumps", r.Jumps, c.Jumps);
    Row("inferred trims", r.InferredTrims, c.InferredTrims);
    Row("colour changes", r.ColorChanges, c.ColorChanges);
    Row("width mm", r.WidthMm, c.WidthMm);
    Row("height mm", r.HeightMm, c.HeightMm);
    Row("thread path m", r.ThreadPathM, c.ThreadPathM);
    Row("stitch length p50 mm", r.StitchLengthMm?.P50 ?? 0, c.StitchLengthMm?.P50 ?? 0);
    Row("satin throw p50 mm (inferred)", r.SatinThrowMm?.P50 ?? 0, c.SatinThrowMm?.P50 ?? 0);
    Row("same-rail spacing p50 mm (inf.)", r.SatinSameRailSpacingMm?.P50 ?? 0, c.SatinSameRailSpacingMm?.P50 ?? 0);
    Row("running share (inferred)", r.RunningShare, c.RunningShare);
    return 0;
}

static int Calibration(string outDir)
{
    Directory.CreateDirectory(outDir);
    var (design, legend) = CalibrationSheet.Build();
    var service = new ProjectService();
    var (encoded, plan) = service.Encode(design);
    File.WriteAllBytes(Path.Combine(outDir, "kalibrasyon.dst"), DstWriter.Write(encoded));
    File.WriteAllBytes(Path.Combine(outDir, "kalibrasyon.embx"), EmbxPackage.Save(design));
    File.WriteAllText(Path.Combine(outDir, "kalibrasyon.md"), CalibrationSheet.LegendMarkdown(legend));
    File.WriteAllText(Path.Combine(outDir, "kalibrasyon-onizleme.svg"), StitchSvgRenderer.Render(encoded, ["#D4A53C"], "#7A101C"));
    Print("kalibrasyon.dst", StitchMetrics.From(encoded));
    Console.WriteLine($"  {legend.Count} test objects; legend in kalibrasyon.md");
    return plan.Diagnostics.Any(d => d.Severity == Severity.Error) ? 2 : 0;
}

static void WriteExtras(Dictionary<string, string> options, EncodedStitchPlan plan, StitchMetrics metrics, IReadOnlyList<string> colors)
{
    if (options.TryGetValue("report", out var report))
    {
        File.WriteAllText(report, JsonSerializer.Serialize(metrics, EmbroideryJson.Indented));
    }

    if (options.TryGetValue("preview", out var preview))
    {
        File.WriteAllText(preview, StitchSvgRenderer.Render(plan, colors, options.GetValueOrDefault("fabric", "#F4F1EA")));
    }
}

static void Print(string name, StitchMetrics m)
{
    var ci = CultureInfo.InvariantCulture;
    Console.WriteLine(string.Create(ci, $"{name}: {m.Stitches:N0} stitches, {m.Jumps} jumps ({m.InferredTrims} inferred trims), {m.ColorChanges} colour changes, {m.WidthMm:0.0} × {m.HeightMm:0.0} mm, thread path {m.ThreadPathM:0.0} m"));
    if (m.StitchLengthMm is { } l) Console.WriteLine(string.Create(ci, $"  stitch length mm  p25 {l.P25:0.00}  p50 {l.P50:0.00}  p75 {l.P75:0.00}  max {l.Max:0.00}"));
    if (m.SatinThrowMm is { } t) Console.WriteLine(string.Create(ci, $"  satin throw mm    p25 {t.P25:0.00}  p50 {t.P50:0.00}  p75 {t.P75:0.00}  (inferred column width)"));
    if (m.SatinSameRailSpacingMm is { } s) Console.WriteLine(string.Create(ci, $"  same-rail spacing p25 {s.P25:0.00}  p50 {s.P50:0.00}  p75 {s.P75:0.00}  (inferred density)"));
    Console.WriteLine(string.Create(ci, $"  running stitches  {m.RunningShare:P1} of stitches (underlay/travel/run, inferred)"));
}

static Dictionary<string, string> Options(string[] args, int start)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = start; i + 1 < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{args[i]}'.");
        options[args[i][2..]] = args[i + 1];
    }

    return options;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
