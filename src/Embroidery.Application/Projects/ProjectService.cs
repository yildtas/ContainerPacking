using System.Collections.Concurrent;
using Embroidery.Application.Caching;
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

    public ProjectService(GenerationCache? cache = null) => _cache = cache ?? new GenerationCache();

    public GenerationCache Cache => _cache;

    public ImportResult ImportSvg(string fileName, string svgText, SvgImportOptions? options = null)
    {
        var artwork = SvgImporter.Import(svgText, options);
        var diagnostics = new List<Diagnostic>(artwork.Diagnostics);
        if (artwork.Diagnostics.Any(d => d.Severity == Severity.Error))
        {
            throw new DesignValidationException(string.Join(" ", artwork.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message)));
        }

        var (threads, objects, objectDiagnostics) = ObjectFactory.FromArtwork(artwork);
        diagnostics.AddRange(objectDiagnostics);
        var design = new Design
        {
            Id = Guid.NewGuid(),
            Name = Path.GetFileNameWithoutExtension(fileName),
            Revision = 1,
            Artwork = new SourceArtwork(fileName, svgText, artwork.ScaleMmPerUnit),
            Threads = threads,
            Objects = objects,
        };
        _sessions[design.Id] = new ProjectSession(design);
        return new ImportResult(design, diagnostics);
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
            objects[index] = ObjectConverter.Convert(objects[index], target);
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
        var plan = BuildPlan(design, ct);
        var profile = MachineProfile.Find(design.MachineProfileId);
        var encoded = MachineEncoder.Encode(plan, profile, design.Name);
        return new ExportResult(DstWriter.Write(encoded), SafeFileName(design.Name) + ".dst", plan.Diagnostics);
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
