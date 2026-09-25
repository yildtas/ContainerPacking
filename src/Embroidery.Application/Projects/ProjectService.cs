using System.Collections.Concurrent;
using Embroidery.Application.Analysis;
using Embroidery.Application.Caching;
using Embroidery.Application.Export;
using Embroidery.Application.Import;
using Embroidery.Application.Preview;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.StitchPlan;
using Embroidery.Formats.Dst;
using Embroidery.Geometry.Svg;
using Embroidery.Machine;
using Embroidery.StitchEngine;
using Embroidery.StitchEngine.Quality;
using Embroidery.StitchEngine.Sequencing;

namespace Embroidery.Application.Projects;

public sealed class ProjectNotFoundException(Guid id) : Exception($"Project {id} is not open.");

public sealed class DesignValidationException(string message) : Exception(message);

public sealed record ImportResult(Design Design, IReadOnlyList<Diagnostic> Diagnostics);

public sealed record ExportResult(byte[] Data, string FileName, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Application use cases. Owns open projects, the generation cache and the pipeline
/// design → blocks (cached) → sequencing → quality → preview / encoder → file.
/// </summary>
public sealed class ProjectService
{
    private readonly ConcurrentDictionary<Guid, ProjectSession> _sessions = new();
    private readonly GenerationCache _cache;
    private readonly ObjectGenerator _generator = new();

    private readonly StitchProfileStore _profiles;

    public ProjectService(GenerationCache? cache = null, StitchProfileStore? profiles = null)
    {
        _cache = cache ?? new GenerationCache();
        _profiles = profiles ?? new StitchProfileStore();
    }

    public StitchProfileStore Profiles => _profiles;

    public GenerationCache Cache => _cache;

    public ImportResult ImportSvg(string fileName, string svgText, SvgImportOptions? options = null, string? stitchProfileId = null)
    {
        var profile = ResolveProfile(stitchProfileId);
        var artwork = SvgImporter.Import(svgText, options);
        var diagnostics = new List<Diagnostic>(artwork.Diagnostics);
        if (artwork.Diagnostics.Any(d => d.Severity == Severity.Error))
        {
            throw new DesignValidationException(string.Join(" ", artwork.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message)));
        }

        var (threads, created, objectDiagnostics) = ObjectFactory.FromArtwork(artwork);
        var objects = created.Select(profile.Apply).ToList();
        diagnostics.AddRange(objectDiagnostics);
        var design = new Design
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileNameWithoutExtension(fileName),
            Revision = 1,
            Artwork = new SourceArtwork(fileName, svgText, artwork.ScaleMmPerUnit),
            Threads = threads,
            Objects = objects,
            StitchProfileId = profile.Id,
            Hoop = SmallestHoopFor(objects),
        };
        _sessions[design.Id] = new ProjectSession(design);
        return new ImportResult(design, diagnostics);
    }

    /// <summary>
    /// Opens a machine file as an editable project by tracing it back to vector objects
    /// (<see cref="DstTracer"/>). Colours are placeholders; the diagnostics say what was recovered.
    /// </summary>
    public ImportResult ImportDst(string fileName, byte[] dst, TraceOptions? options = null, string? stitchProfileId = null)
    {
        var profile = ResolveProfile(stitchProfileId);
        EncodedStitchPlan plan;
        try
        {
            plan = DstReader.Read(dst).Plan;
        }
        catch (DstFormatException ex)
        {
            throw new DesignValidationException($"Not a readable DST file: {ex.Message}");
        }

        var traced = DstTracer.Trace(plan, Path.GetFileNameWithoutExtension(fileName), options);
        if (traced.Design.Objects.Count == 0) throw new DesignValidationException("The DST file contains no stitches that could be traced.");
        var design = traced.Design with
        {
            Revision = 1,
            Objects = traced.Design.Objects.Select(profile.Apply).ToList(),
            StitchProfileId = profile.Id,
        };
        _sessions[design.Id] = new ProjectSession(design);
        return new ImportResult(design, traced.Diagnostics);
    }

    public Design Open(Stream embx)
    {
        var design = EmbxPackage.Load(embx);
        // Opening a copy of an already open project gets its own identity.
        if (_sessions.ContainsKey(design.Id)) design = design with { Id = Guid.NewGuid() };
        design = design with { Revision = Math.Max(1, design.Revision) };
        Validate(design);
        _sessions[design.Id] = new ProjectSession(design);
        return design;
    }

    public Design Get(Guid projectId) => Session(projectId).Current;

    public bool CanUndo(Guid projectId) => Session(projectId).CanUndo;
    public bool CanRedo(Guid projectId) => Session(projectId).CanRedo;

    public void Close(Guid projectId) => _sessions.TryRemove(projectId, out _);

    /// <summary>Replaces one object (same id). The object's type may change.</summary>
    public Design UpdateObject(Guid projectId, long? expectedRevision, EmbroideryObject updated) =>
        Session(projectId).Apply(expectedRevision, d =>
        {
            var index = IndexOf(d, updated.Id);
            var objects = d.Objects.ToList();
            objects[index] = updated;
            var next = d with { Objects = objects };
            Validate(next);
            return next;
        });

    public Design ConvertObject(Guid projectId, long? expectedRevision, Guid objectId, StitchType target) =>
        Session(projectId).Apply(expectedRevision, d =>
        {
            var index = IndexOf(d, objectId);
            var objects = d.Objects.ToList();
            var converted = ObjectConverter.ConvertMany(objects[index], target);
            objects.RemoveAt(index);
            objects.InsertRange(index, converted);
            return d with { Objects = objects };
        });

    public Design DeleteObject(Guid projectId, long? expectedRevision, Guid objectId) =>
        Session(projectId).Apply(expectedRevision, d =>
        {
            IndexOf(d, objectId);
            return d with { Objects = d.Objects.Where(o => o.Id != objectId).ToList() };
        });

    /// <summary>Sets the sew order. The list must be a permutation of the current object ids.</summary>
    public Design Reorder(Guid projectId, long? expectedRevision, IReadOnlyList<Guid> order) =>
        Session(projectId).Apply(expectedRevision, d =>
        {
            if (order.Count != d.Objects.Count || order.Distinct().Count() != order.Count || order.Any(id => d.FindObject(id) is null))
            {
                throw new DesignValidationException("Order must list every object exactly once.");
            }

            var byId = d.Objects.ToDictionary(o => o.Id);
            return d with { Objects = order.Select(id => byId[id]).ToList() };
        });

    public Design UpdateThreads(Guid projectId, long? expectedRevision, IReadOnlyList<EmbroideryThread> threads) =>
        Session(projectId).Apply(expectedRevision, d =>
        {
            var next = d with { Threads = threads };
            Validate(next);
            return next;
        });

    public Design UpdateSettings(Guid projectId, long? expectedRevision, Hoop? hoop, ConnectionPolicy? connections, string? name) =>
        Session(projectId).Apply(expectedRevision, d => d with
        {
            Hoop = hoop ?? d.Hoop,
            Connections = connections ?? d.Connections,
            Name = string.IsNullOrWhiteSpace(name) ? d.Name : name.Trim(),
        });

    /// <summary>
    /// Reorders objects to cut colour changes and travel without breaking the stacking of
    /// overlapping objects. Applied (as one undoable step) only when it lowers the cost.
    /// </summary>
    public (Design Design, SequenceResult Result) OptimizeOrder(Guid projectId, long? expectedRevision)
    {
        SequenceResult? result = null;
        var design = Session(projectId).Apply(expectedRevision, d =>
        {
            result = SequenceOptimizer.Optimize(d);
            if (!result.Improved) return d;
            var byId = d.Objects.ToDictionary(o => o.Id);
            return d with { Objects = result.Order.Select(id => byId[id]).ToList() };
        });
        return (design, result!);
    }

    public Design Mirror(Guid projectId, long? expectedRevision, MirrorAxis axis) =>
        Session(projectId).Apply(expectedRevision, d => DesignTransforms.Mirror(d, axis));

    /// <summary>Applies a stitch profile's density parameters to every object (one undoable step).</summary>
    public Design ApplyStitchProfile(Guid projectId, long? expectedRevision, string profileId)
    {
        var profile = ResolveProfile(profileId);
        return Session(projectId).Apply(expectedRevision, d => d with
        {
            StitchProfileId = profile.Id,
            Objects = d.Objects.Select(profile.Apply).ToList(),
        });
    }

    /// <summary>The smallest preset hoop or frame the objects fit in (the largest when none does).</summary>
    public static Hoop SmallestHoopFor(IReadOnlyList<EmbroideryObject> objects)
    {
        var b = objects.Aggregate(Core.Primitives.Bounds.Empty, (acc, o) => acc.Include(o.Bounds));
        if (b.IsEmpty) return Hoop.Default;
        return HoopPresets.All
            .Where(h => (h.WidthMm >= b.Width && h.HeightMm >= b.Height) || (h.WidthMm >= b.Height && h.HeightMm >= b.Width))
            .OrderBy(h => h.WidthMm * h.HeightMm)
            .FirstOrDefault() ?? HoopPresets.All.MaxBy(h => h.WidthMm * h.HeightMm)!;
    }

    private StitchProfile ResolveProfile(string? id) =>
        id is null ? StitchProfile.Standard
            : _profiles.Find(id) ?? throw new DesignValidationException($"Unknown stitch profile '{id}'.");

    public Design Undo(Guid projectId) => Session(projectId).Undo();
    public Design Redo(Guid projectId) => Session(projectId).Redo();

    /// <summary>Full pipeline up to a validated logical plan. Unchanged objects come from the cache.</summary>
    public LogicalStitchPlan BuildPlan(Design design, CancellationToken ct = default)
    {
        var profile = MachineProfile.Find(design.MachineProfileId);
        var plan = PlanBuilder.Build(design, Generate, profile.Connections, ct);
        return QualityPass.Run(plan, design, new QualityOptions { LongStitchMm = profile.MaxStitchMm });
    }

    public PreviewDto Preview(Guid projectId, CancellationToken ct = default)
    {
        var design = Get(projectId);
        return PreviewBuilder.Build(design.Revision, design, BuildPlan(design, ct));
    }

    public ExportResult ExportDst(Guid projectId, CancellationToken ct = default)
    {
        var design = Get(projectId);
        var (encoded, plan) = Encode(design, ct);
        return new ExportResult(DstWriter.Write(encoded), SafeFileName(design.Name) + ".dst", plan.Diagnostics);
    }

    /// <summary>Full pipeline for any design (open or not): validated plan and machine stream.</summary>
    public (EncodedStitchPlan Encoded, LogicalStitchPlan Plan) Encode(Design design, CancellationToken ct = default)
    {
        Validate(design);
        var plan = BuildPlan(design, ct);
        var profile = MachineProfile.Find(design.MachineProfileId);
        return (MachineEncoder.Encode(plan, profile, design.Name), plan);
    }

    /// <summary>
    /// For designs larger than the hoop: one DST per hooping in a ZIP, with a text file telling
    /// the operator the order, grid position and registration marks of each part.
    /// </summary>
    public ExportResult ExportDstParts(Guid projectId, CancellationToken ct = default)
    {
        var design = Get(projectId);
        var split = HoopSplitter.Split(design, design.Hoop);
        if (!split.Succeeded)
        {
            throw new DesignValidationException(string.Join(" ", split.Diagnostics.Select(d => d.Message)));
        }

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var guide = new System.Text.StringBuilder();
            guide.AppendLine(FormattableString.Invariant($"{design.Name}: {split.Parts.Count} kasnaklama, kasnak {split.Hoop.WidthMm:0} × {split.Hoop.HeightMm:0} mm"));
            guide.AppendLine("Her parçada önce 'Hizalama' ipliğiyle artı işaretleri dikilir; bir sonraki parçayı bu işaretler üst üste gelecek şekilde kasnaklayın.");
            guide.AppendLine();
            foreach (var part in split.Parts)
            {
                var (encoded, _) = Encode(part.Design, ct);
                var entry = zip.CreateEntry($"{SafeFileName(part.Design.Name)}.dst");
                using (var s = entry.Open()) s.Write(DstWriter.Write(encoded));
                var center = part.Area.Center;
                guide.AppendLine(FormattableString.Invariant(
                    $"{part.Number}. {part.Design.Name}.dst  sütun {part.Column + 1}, satır {part.Row + 1}  merkez ({center.X:0.0}, {center.Y:0.0}) mm  {part.Area.Width:0} × {part.Area.Height:0} mm  {part.Design.Objects.Count(o => o.Name.StartsWith(HoopSplitter.AlignmentThreadName, StringComparison.Ordinal))} işaret"));
            }

            var readme = zip.CreateEntry("KASNAKLAMA.txt");
            using var w = new StreamWriter(readme.Open(), new System.Text.UTF8Encoding(false));
            w.Write(guide.ToString());
        }

        return new ExportResult(ms.ToArray(), SafeFileName(design.Name) + "-parcalar.zip", split.Diagnostics);
    }

    /// <summary>The design as SVG in the vector delivery convention (re-importable).</summary>
    public ExportResult ExportSvg(Guid projectId)
    {
        var design = Get(projectId);
        return new ExportResult(System.Text.Encoding.UTF8.GetBytes(DesignSvgWriter.Write(design)), SafeFileName(design.Name) + ".svg", []);
    }

    public ExportResult ExportEmbx(Guid projectId)
    {
        var design = Get(projectId);
        return new ExportResult(EmbxPackage.Save(design), SafeFileName(design.Name) + ".embx", []);
    }

    private GenerationResult<LogicalStitchBlock> Generate(EmbroideryObject item, GenerationContext context, CancellationToken ct) =>
        _cache.GetOrAdd(ObjectGenerationKey.For(item, context), () => _generator.Generate(item, context, ct));

    private ProjectSession Session(Guid id) =>
        _sessions.TryGetValue(id, out var s) ? s : throw new ProjectNotFoundException(id);

    private static int IndexOf(Design d, Guid objectId)
    {
        for (var i = 0; i < d.Objects.Count; i++)
        {
            if (d.Objects[i].Id == objectId) return i;
        }

        throw new DesignValidationException($"Object {objectId} does not exist.");
    }

    private static void Validate(Design design)
    {
        if (design.Threads.Count == 0) throw new DesignValidationException("A design needs at least one thread.");
        foreach (var t in design.Threads)
        {
            if (t.ColorHex.Length != 7 || t.ColorHex[0] != '#' || !t.ColorHex[1..].All(char.IsAsciiHexDigit))
            {
                throw new DesignValidationException($"Thread colour '{t.ColorHex}' must be #RRGGBB.");
            }
        }

        foreach (var o in design.Objects)
        {
            if (o.ThreadIndex < 0 || o.ThreadIndex >= design.Threads.Count)
            {
                throw new DesignValidationException($"Object '{o.Name}' refers to missing thread {o.ThreadIndex}.");
            }

            ParameterValidator.Validate(o);
        }

        if (design.Objects.Select(o => o.Id).Distinct().Count() != design.Objects.Count)
        {
            throw new DesignValidationException("Object ids must be unique.");
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "design" : clean;
    }
}
