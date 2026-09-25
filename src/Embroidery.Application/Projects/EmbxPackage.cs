using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Embroidery.Application.Serialization;
using Embroidery.Core.Model;
using Embroidery.StitchEngine;

namespace Embroidery.Application.Projects;

public sealed record EmbxManifest(
    int SchemaVersion,
    string GeneratorVersion,
    string CreatedWith,
    string? ArtworkFileName,
    double ArtworkScaleMmPerUnit);

public sealed class EmbxFormatException(string message) : Exception(message);

/// <summary>
/// The editable project package (.embx, a ZIP): manifest.json, design.json and the original
/// artwork. Stitch plans are not stored; they are regenerated from the design.
/// </summary>
public static class EmbxPackage
{
    public const int CurrentSchemaVersion = 1;
    private const long MaxEntryBytes = 64L * 1024 * 1024;

    public static byte[] Save(Design design)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new EmbxManifest(CurrentSchemaVersion, StitchEngineInfo.GeneratorVersion, "Embroidery",
                design.Artwork?.FileName, design.Artwork?.ScaleMmPerUnit ?? 0);
            WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, EmbroideryJson.Indented));
            WriteEntry(zip, "design.json", JsonSerializer.SerializeToUtf8Bytes(design with { Artwork = null }, EmbroideryJson.Indented));
            if (design.Artwork is { } art)
            {
                WriteEntry(zip, "artwork/source.svg", Encoding.UTF8.GetBytes(art.SvgText));
            }
        }

        return ms.ToArray();
    }

    public static Design Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = JsonSerializer.Deserialize<EmbxManifest>(ReadEntry(zip, "manifest.json")
            ?? throw new EmbxFormatException("manifest.json is missing."), EmbroideryJson.Options)
            ?? throw new EmbxFormatException("manifest.json is empty.");

        if (manifest.SchemaVersion > CurrentSchemaVersion)
        {
            throw new EmbxFormatException($"Project schema {manifest.SchemaVersion} is newer than this application supports ({CurrentSchemaVersion}).");
        }

        var json = ReadEntry(zip, "design.json") ?? throw new EmbxFormatException("design.json is missing.");
        json = Migrate(json, manifest.SchemaVersion);
        var design = JsonSerializer.Deserialize<Design>(json, EmbroideryJson.Options)
            ?? throw new EmbxFormatException("design.json is empty.");

        if (ReadEntry(zip, "artwork/source.svg") is { } svg)
        {
            design = design with { Artwork = new SourceArtwork(manifest.ArtworkFileName ?? "source.svg", Encoding.UTF8.GetString(svg), manifest.ArtworkScaleMmPerUnit) };
        }

        return design;
    }

    /// <summary>Upgrades older design.json layouts step by step. Schema 1 is the first version.</summary>
    private static byte[] Migrate(byte[] json, int fromVersion) => fromVersion switch
    {
        _ => json,
    };

    private static void WriteEntry(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(data);
    }

    private static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return null;
        if (entry.Length > MaxEntryBytes) throw new EmbxFormatException($"{name} is too large.");
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
