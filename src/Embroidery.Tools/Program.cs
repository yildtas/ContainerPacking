using System.Globalization;
using System.Text.Json;
using Embroidery.Application.Analysis;
using Embroidery.Application.Backends;
using Embroidery.Application.Calibration;
using Embroidery.Application.Export;
using Embroidery.Application.Projects;
using Embroidery.Application.Serialization;
using Embroidery.Core.Diagnostics;
using Embroidery.Formats;
using Embroidery.Formats.Dst;
using Embroidery.Geometry.Svg;
using Embroidery.Machine;

// Command-line tools: batch conversion, golden-master measurement and calibration sheets.
// Usage is printed when arguments are missing.

const string Usage = """
    embroidery convert <in.svg> <out.dst|.pes|.jef|.exp> [--width mm] [--profile id] [--profiles dir] [--mirror h|v] [--hoop WxH] [--optimize] [--split]
                       [--report r.json] [--preview p.svg] [--fabric #RRGGBB]
    embroidery analyze <in.dst> [--report r.json] [--preview p.svg] [--fabric #RRGGBB] [--thread #RRGGBB]
    embroidery compare <reference.dst> <candidate.dst|candidate.svg>
    embroidery compare-backends <in.svg> [--inkstitch "command {input} {output}"] [--ewa settings.json] [--out dir]
    embroidery trace <in.dst> <out.svg> [--pull mm] [--regenerate out.dst]
    embroidery calibration <out-dir>
    embroidery profile list [--profiles dir]
    embroidery profile create <id> <name> [--from id] [--satin-spacing mm] [--satin-pull mm] [--tatami-row mm]
                       [--tatami-length mm] [--tatami-pull mm] [--run-length mm] [--rope-spacing mm] [--profiles dir]
    """;

try
{
    return args.Length == 0 ? Fail(Usage) : args[0] switch
    {
        "convert" when args.Length >= 3 => Convert(args[1], args[2], Options(args, 3)),
        "analyze" when args.Length >= 2 => Analyze(args[1], Options(args, 2)),
        "compare" when args.Length >= 3 => Compare(args[1], args[2]),
        "compare-backends" when args.Length >= 2 => CompareBackends(args[1], Options(args, 2)).GetAwaiter().GetResult(),
        "trace" when args.Length >= 3 => Trace(args[1], args[2], Options(args, 3)),
        "calibration" when args.Length >= 2 => Calibration(args[1]),
        "profile" when args.Length >= 2 && args[1] == "list" => ProfileList(Options(args, 2)),
        "profile" when args.Length >= 4 && args[1] == "create" => ProfileCreate(args[2], args[3], Options(args, 4)),
        _ => Fail(Usage),
    };
}
catch (Exception ex) when (ex is IOException or FormatException or DesignValidationException or UnauthorizedAccessException or ArgumentException)
{
    return Fail(ex.Message);
}

static StitchProfileStore Store(Dictionary<string, string> options) =>
    new(options.GetValueOrDefault("profiles", "profiles"));

static int ProfileList(Dictionary<string, string> options)
{
    foreach (var p in Store(options).All)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{p.Id,-16} {p.Name,-28} satin {p.SatinSpacingMm:0.00}/{p.SatinPullMm:0.00}  tatami {p.TatamiRowSpacingMm:0.00}/{p.TatamiStitchLengthMm:0.0}/{p.TatamiPullMm:0.00}  run {p.RunStitchLengthMm:0.0}  rope {p.RopeSpacingMm:0.00}"));
    }

    return 0;
}

static int ProfileCreate(string id, string name, Dictionary<string, string> options)
{
    var store = Store(options);
    var baseProfile = store.Find(options.GetValueOrDefault("from", "standard"))
        ?? throw new ArgumentException($"Unknown base profile '{options["from"]}'.");
    double Get(string key, double fallback) =>
        options.TryGetValue(key, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
    var profile = baseProfile with
    {
        Id = id,
        Name = name,
        Description = options.GetValueOrDefault("description", $"Kalibrasyondan türetildi ({DateTime.Now:yyyy-MM-dd})."),
        SatinSpacingMm = Get("satin-spacing", baseProfile.SatinSpacingMm),
        SatinPullMm = Get("satin-pull", baseProfile.SatinPullMm),
        TatamiRowSpacingMm = Get("tatami-row", baseProfile.TatamiRowSpacingMm),
        TatamiStitchLengthMm = Get("tatami-length", baseProfile.TatamiStitchLengthMm),
        TatamiPullMm = Get("tatami-pull", baseProfile.TatamiPullMm),
        RunStitchLengthMm = Get("run-length", baseProfile.RunStitchLengthMm),
        RopeSpacingMm = Get("rope-spacing", baseProfile.RopeSpacingMm),
    };
    store.Save(profile);
    Console.WriteLine($"Saved profile '{id}' to {Path.GetFullPath(options.GetValueOrDefault("profiles", "profiles"))}.");
    return 0;
}

static int Convert(string input, string output, Dictionary<string, string> options)
{
    var service = new ProjectService(profiles: Store(options));
    double? width = options.TryGetValue("width", out var w) ? double.Parse(w, CultureInfo.InvariantCulture) : null;
    var imported = service.ImportSvg(Path.GetFileName(input), File.ReadAllText(input), new SvgImportOptions { TargetWidthMm = width },
        options.GetValueOrDefault("profile"));
    var design = imported.Design;
    if (options.TryGetValue("mirror", out var mirror))
    {
        design = service.Mirror(design.Id, null, mirror.ToLowerInvariant() switch
        {
            "h" => MirrorAxis.Horizontal,
            "v" => MirrorAxis.Vertical,
            _ => throw new ArgumentException("--mirror must be h or v."),
        });
    }

    if (options.ContainsKey("optimize"))
    {
        var (optimized, r) = service.OptimizeOrder(design.Id, null);
        design = optimized;
        Console.WriteLine(r.Improved
            ? $"  order optimised: colour changes {r.Before.ColorChanges} -> {r.After.ColorChanges}, travel {r.Before.TravelMm:0} -> {r.After.TravelMm:0} mm"
            : "  order already optimal");
    }

    if (options.TryGetValue("hoop", out var hoopText))
    {
        var parts = hoopText.ToLowerInvariant().Split('x');
        if (parts.Length != 2) throw new ArgumentException("--hoop must look like 300x500 (mm).");
        var hoop = new Embroidery.Core.Model.Hoop(hoopText, double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
        design = service.UpdateSettings(design.Id, null, hoop, null, null);
    }

    if (options.ContainsKey("split"))
    {
        var split = HoopSplitter.Split(design, design.Hoop);
        foreach (var d in split.Diagnostics) Console.WriteLine($"  {d.Severity,-7} {d.Code} {d.Message}");
        if (!split.Succeeded) return 2;
        foreach (var part in split.Parts)
        {
            var partFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, $"{Path.GetFileNameWithoutExtension(output)}-{part.Number}.dst");
            var (partEncoded, _) = service.Encode(part.Design);
            File.WriteAllBytes(partFile, DstWriter.Write(partEncoded));
            Console.WriteLine(FormattableString.Invariant($"  part {part.Number} (col {part.Column + 1}, row {part.Row + 1}): {Path.GetFileName(partFile)}, {part.Area.Width:0} × {part.Area.Height:0} mm"));
        }

        return 0;
    }

    // The output extension picks the machine format (dst, pes, jef, exp).
    var format = StitchFormats.Find(Path.GetExtension(output)) ?? StitchFormats.Dst;
    var (encoded, plan) = service.Encode(design, format.Profile);
    File.WriteAllBytes(output, format.Write(encoded, ProjectService.BlockColors(design, plan)));

    var metrics = StitchMetrics.From(encoded);
    Print(Path.GetFileName(output), metrics);
    Console.WriteLine($"  hoop {design.Hoop.Name} ({design.Hoop.WidthMm:0} × {design.Hoop.HeightMm:0} mm), profile {design.StitchProfileId}");
    foreach (var group in imported.Diagnostics.Concat(plan.Diagnostics).Where(d => d.Severity != Severity.Info).GroupBy(d => (d.Severity, d.Code, d.Message)))
    {
        var count = group.Count() > 1 ? $" (×{group.Count()})" : "";
        Console.WriteLine($"  {group.Key.Severity,-7} {group.Key.Code} {group.Key.Message}{count}");
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

static async Task<int> CompareBackends(string input, Dictionary<string, string> options)
{
    var ewaSettings = options.TryGetValue("ewa", out var ewaFile)
        ? JsonSerializer.Deserialize<WilcomEwaSettings>(File.ReadAllText(ewaFile), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new WilcomEwaSettings()
        : new WilcomEwaSettings();
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    IDigitizerBackend[] backends =
    [
        new NativeBackend(new ProjectService(profiles: Store(options)), options.GetValueOrDefault("profile")),
        new CommandBackend("inkstitch", "Ink/Stitch", options.GetValueOrDefault("inkstitch")),
        new WilcomEwaBackend(http, ewaSettings),
    ];

    var rows = await DigitizerBackends.CompareAsync(backends, Path.GetFileName(input), File.ReadAllText(input));
    Console.WriteLine($"{"backend",-18}{"stitches",10}{"jumps",8}{"trims",8}{"thread m",10}{"throw p50",11}{"spacing p50",13}{"seconds",9}");
    foreach (var (backend, result, m) in rows)
    {
        if (m is null)
        {
            Console.WriteLine($"{backend.Name,-18}{(backend.IsConfigured ? "failed: " : "")}{result.Message}");
            continue;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{backend.Name,-18}{m.Stitches,10}{m.Jumps,8}{m.InferredTrims,8}{m.ThreadPathM,10:0.00}{m.SatinThrowMm?.P50 ?? 0,11:0.00}{m.SatinSameRailSpacingMm?.P50 ?? 0,13:0.00}{result.Elapsed.TotalSeconds,9:0.0}"));
        if (options.TryGetValue("out", out var outDir) && result.Dst is { } dst)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(input)}-{backend.Id}.dst"), dst);
        }
    }

    return rows.Any(r => r.Backend.IsConfigured && !r.Result.Succeeded) ? 2 : 0;
}

static int Trace(string input, string output, Dictionary<string, string> options)
{
    var pull = options.TryGetValue("pull", out var p) ? double.Parse(p, CultureInfo.InvariantCulture) : 0;
    var original = DstReader.Read(File.ReadAllBytes(input)).Plan;
    var traced = DstTracer.Trace(original, Path.GetFileNameWithoutExtension(input), new TraceOptions { PullCompensationMm = pull });
    File.WriteAllText(output, DesignSvgWriter.Write(traced.Design));
    Console.WriteLine($"{Path.GetFileName(output)}: {traced.SatinColumns} satin column(s), {traced.RunObjects} run(s), {traced.Design.Threads.Count} thread(s)");
    foreach (var d in traced.Diagnostics) Console.WriteLine($"  {d.Severity,-7} {d.Code} {d.Message}");

    if (options.TryGetValue("regenerate", out var regenerated))
    {
        // Sew the traced vectors with our engine and compare with the original machine file.
        var service = new ProjectService(profiles: Store(options));
        var design = service.ImportSvg(Path.GetFileName(output), File.ReadAllText(output), stitchProfileId: options.GetValueOrDefault("profile")).Design;
        var (encoded, _) = service.Encode(design);
        File.WriteAllBytes(regenerated, DstWriter.Write(encoded));
        Console.WriteLine();
        return Compare(input, regenerated);
    }

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
    for (var i = start; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{args[i]}'.");
        // A flag followed by another flag (or nothing) has no value.
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        options[args[i][2..]] = hasValue ? args[++i] : "true";
    }

    return options;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
