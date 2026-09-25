using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Embroidery.Application.Projects;

namespace Embroidery.Application.Backends;

public sealed record BackendResult(string BackendId, bool Succeeded, byte[]? Dst, string Message, TimeSpan Elapsed);

/// <summary>
/// Something that turns vector artwork into a machine file. Our engine is the product; the others
/// exist to compare quality on the same input (ARCHITECTURE D1). A backend without its settings
/// reports <see cref="IsConfigured"/> = false and says what is missing instead of failing.
/// </summary>
public interface IDigitizerBackend
{
    string Id { get; }
    string Name { get; }
    bool IsConfigured { get; }

    /// <summary>What to set up when <see cref="IsConfigured"/> is false.</summary>
    string ConfigurationHint { get; }

    Task<BackendResult> DigitizeAsync(string fileName, string svg, CancellationToken ct = default);
}

/// <summary>Our own engine.</summary>
public sealed class NativeBackend(ProjectService? service = null, string? stitchProfileId = null) : IDigitizerBackend
{
    private readonly ProjectService _service = service ?? new ProjectService();

    public string Id => "native";
    public string Name => "Kendi motorumuz";
    public bool IsConfigured => true;
    public string ConfigurationHint => "";

    public Task<BackendResult> DigitizeAsync(string fileName, string svg, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var design = _service.ImportSvg(fileName, svg, stitchProfileId: stitchProfileId).Design;
            var export = _service.ExportDst(design.Id, ct);
            _service.Close(design.Id);
            return Task.FromResult(new BackendResult(Id, true, export.Data, $"{design.Objects.Count} objects", watch.Elapsed));
        }
        catch (DesignValidationException ex)
        {
            return Task.FromResult(new BackendResult(Id, false, null, ex.Message, watch.Elapsed));
        }
    }
}

/// <summary>
/// Any command-line digitizer, e.g. Ink/Stitch: the command template gets <c>{input}</c> (an SVG
/// file) and <c>{output}</c> (the DST it must write). Example for Ink/Stitch:
/// <c>/opt/inkstitch/bin/inkstitch --extension=zip --format-dst=True {input} &gt; {output}</c>
/// does not work without a shell, so point the template at a small wrapper script instead.
/// Ink/Stitch is GPL: it runs as a separate process and none of its code is linked.
/// </summary>
public sealed class CommandBackend(string id, string name, string? commandTemplate, TimeSpan? timeout = null) : IDigitizerBackend
{
    public string Id => id;
    public string Name => name;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(commandTemplate);
    public string ConfigurationHint => $"Give a command line using {{input}} and {{output}} (CLI: --{id} \"...\").";

    public async Task<BackendResult> DigitizeAsync(string fileName, string svg, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        if (!IsConfigured) return new BackendResult(Id, false, null, "Not configured. " + ConfigurationHint, watch.Elapsed);

        var dir = Directory.CreateTempSubdirectory("embroidery-backend-");
        try
        {
            var input = Path.Combine(dir.FullName, "input.svg");
            var output = Path.Combine(dir.FullName, "output.dst");
            await File.WriteAllTextAsync(input, svg, ct);
            var parts = Split(commandTemplate!).Select(p => p.Replace("{input}", input).Replace("{output}", output)).ToList();
            var start = new ProcessStartInfo(parts[0]) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var arg in parts.Skip(1)) start.ArgumentList.Add(arg);

            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{parts[0]}'.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            _ = process.StandardOutput.ReadToEndAsync(limit.Token);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new BackendResult(Id, false, null, "Timed out.", watch.Elapsed);
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                return new BackendResult(Id, false, null, $"Exit code {process.ExitCode}: {(await stderr).Trim()}", watch.Elapsed);
            }

            return new BackendResult(Id, true, await File.ReadAllBytesAsync(output, ct), "ok", watch.Elapsed);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new BackendResult(Id, false, null, ex.Message, watch.Elapsed);
        }
        finally
        {
            try { dir.Delete(recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Splits a command line on spaces, keeping double-quoted parts together.</summary>
    internal static List<string> Split(string command)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in command)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ' ' && !quoted)
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}

public sealed record WilcomEwaSettings
{
    public string BaseUrl { get; init; } = "https://public.ewa.wilcomapps.com/";
    public string? AppId { get; init; }
    public string? AppKey { get; init; }

    /// <summary>
    /// requestXml for <c>vectorArtDesign</c> with placeholders <c>{file_base64}</c>,
    /// <c>{file_name}</c> and <c>{format}</c>. The schema is in Wilcom's interface specification,
    /// which needs an approved account, so it is supplied by configuration, not hard-coded.
    /// </summary>
    public string? RequestTemplate { get; init; }

    /// <summary>
    /// EWA takes vector art as EPS/PDF only: a command converting <c>{input}</c> (SVG) to
    /// <c>{output}</c> (PDF), e.g. <c>inkscape {input} --export-filename={output}</c>.
    /// </summary>
    public string? SvgToPdfCommand { get; init; }
}

/// <summary>
/// Wilcom Embroidery Web API (EWA), <c>vectorArtDesign</c>: POST appId, appKey and requestXml;
/// the response XML lists the files. Limits noted in the research: 2 MB, 22 500 mm² for automatic
/// digitizing, 90 s. Not verified against the live service (the account was still pending), so
/// the request body comes from <see cref="WilcomEwaSettings.RequestTemplate"/> and the response
/// reader accepts a <c>file</c> element carrying base64 content or a <c>url</c>.
/// </summary>
public sealed class WilcomEwaBackend(HttpClient http, WilcomEwaSettings settings) : IDigitizerBackend
{
    public string Id => "wilcom-ewa";
    public string Name => "Wilcom EWA";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(settings.AppId) && !string.IsNullOrWhiteSpace(settings.AppKey)
        && !string.IsNullOrWhiteSpace(settings.RequestTemplate) && !string.IsNullOrWhiteSpace(settings.SvgToPdfCommand);

    public string ConfigurationHint =>
        "Needs appId and appKey (developer.wilcom.com, approved account), requestTemplate and svgToPdfCommand (CLI: --ewa settings.json).";

    public async Task<BackendResult> DigitizeAsync(string fileName, string svg, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        if (!IsConfigured) return new BackendResult(Id, false, null, "Not configured. " + ConfigurationHint, watch.Elapsed);

        var pdf = await ToPdf(svg, ct);
        if (pdf is null) return new BackendResult(Id, false, null, "SVG → PDF conversion failed.", watch.Elapsed);
        var name = Path.GetFileNameWithoutExtension(fileName) + ".pdf";
        var requestXml = settings.RequestTemplate!
            .Replace("{file_base64}", Convert.ToBase64String(pdf))
            .Replace("{file_name}", System.Security.SecurityElement.Escape(name))
            .Replace("{format}", "DST");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["appId"] = settings.AppId!,
            ["appKey"] = settings.AppKey!,
            ["requestXml"] = requestXml,
        });
        try
        {
            using var response = await http.PostAsync(new Uri(new Uri(settings.BaseUrl), "vectorArtDesign"), form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new BackendResult(Id, false, null, $"HTTP {(int)response.StatusCode}: {ErrorInfo(body)}", watch.Elapsed);
            }

            var dst = await ReadDst(body, ct);
            return dst is null
                ? new BackendResult(Id, false, null, "The response contained no DST file.", watch.Elapsed)
                : new BackendResult(Id, true, dst, "ok", watch.Elapsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or FormatException)
        {
            return new BackendResult(Id, false, null, ex.Message, watch.Elapsed);
        }
    }

    private async Task<byte[]?> ToPdf(string svg, CancellationToken ct)
    {
        var converter = new CommandBackend("svg-to-pdf", "SVG → PDF", settings.SvgToPdfCommand);
        var result = await converter.DigitizeAsync("art.svg", svg, ct);
        return result.Dst; // CommandBackend returns whatever the command wrote to {output}
    }

    private static string ErrorInfo(string body)
    {
        try
        {
            return XDocument.Parse(body).Descendants().FirstOrDefault(e => e.Name.LocalName == "error_info")?.Value.Trim() ?? body;
        }
        catch (System.Xml.XmlException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    private async Task<byte[]?> ReadDst(string body, CancellationToken ct)
    {
        var files = XDocument.Parse(body).Descendants().Where(e => e.Name.LocalName == "file").ToList();
        var file = files.FirstOrDefault(f => (f.Attribute("name")?.Value ?? f.Attribute("format")?.Value ?? "")
            .EndsWith("dst", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();
        if (file is null) return null;
        if (file.Attribute("url")?.Value is { } url) return await http.GetByteArrayAsync(new Uri(url), ct);
        var data = file.Attribute("data")?.Value ?? file.Value;
        return string.IsNullOrWhiteSpace(data) ? null : Convert.FromBase64String(data.Trim());
    }
}

public static class DigitizerBackends
{
    /// <summary>Runs every backend on the same artwork and reports metrics side by side.</summary>
    public static async Task<IReadOnlyList<(IDigitizerBackend Backend, BackendResult Result, Analysis.StitchMetrics? Metrics)>> CompareAsync(
        IEnumerable<IDigitizerBackend> backends, string fileName, string svg, CancellationToken ct = default)
    {
        var rows = new List<(IDigitizerBackend, BackendResult, Analysis.StitchMetrics?)>();
        foreach (var backend in backends)
        {
            var result = await backend.DigitizeAsync(fileName, svg, ct);
            Analysis.StitchMetrics? metrics = null;
            if (result is { Succeeded: true, Dst: { } dst })
            {
                try
                {
                    metrics = Analysis.StitchMetrics.From(Formats.Dst.DstReader.Read(dst).Plan);
                }
                catch (Formats.Dst.DstFormatException ex)
                {
                    result = result with { Succeeded = false, Message = "Output is not a DST file: " + ex.Message };
                }
            }

            rows.Add((backend, result, metrics));
        }

        return rows;
    }

}
