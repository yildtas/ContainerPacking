using System.Globalization;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Geometry;
using Embroidery.Geometry.Svg;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.Application.Import;

/// <summary>
/// Turns imported artwork into editable embroidery objects.
///
/// Vector delivery convention (see docs/ARCHITECTURE.md §7):
/// <list type="bullet">
/// <item><c>data-stitch="run|satin|tatami"</c> on an element fixes its stitch type.
/// Ink/Stitch's <c>inkstitch:satin_column="True"</c> is read as <c>satin</c>.</item>
/// <item>A satin with two or more subpaths uses the Ink/Stitch rails convention: the two longest
/// subpaths are the rails, every other subpath is a rung.</item>
/// <item>A satin with one subpath is a centre line; its width is the stroke width
/// (or <c>data-width</c> in mm), tapers come from <c>data-taper</c>, <c>data-taper-start</c>,
/// <c>data-taper-end</c> (mm).</item>
/// <item>Without a hint: narrow filled shapes become automatic satin columns when they fit
/// well (<see cref="AutoColumns"/>), other filled shapes Tatami, strokes of at least
/// <see cref="SatinStrokeMinMm"/> satin centre lines, thinner strokes Run.</item>
/// </list>
/// Codes: IMP001 shape skipped, IMP002 rails guessed from an outline, IMP003 unknown hint,
/// IMP004 narrow shape kept as Tatami (why), IMP005 satin columns made from a filled outline.
/// </summary>
public static class ObjectFactory
{
    /// <summary>Strokes at least this wide become satin columns.</summary>
    public const double SatinStrokeMinMm = 1.2;

    public static (IReadOnlyList<EmbroideryThread> Threads, IReadOnlyList<EmbroideryObject> Objects, IReadOnlyList<Diagnostic> Diagnostics)
        FromArtwork(ImportedArtwork artwork, bool autoSatinColumns = true)
    {
        var threads = new List<EmbroideryThread>();
        var objects = new List<EmbroideryObject>();
        var diagnostics = new List<Diagnostic>();

        int ThreadFor(string color)
        {
            var idx = threads.FindIndex(t => t.ColorHex.Equals(color, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) return idx;
            threads.Add(new EmbroideryThread($"İplik {threads.Count + 1}", color));
            return threads.Count - 1;
        }

        var n = 0;
        foreach (var shape in artwork.Shapes)
        {
            var name = shape.ElementId ?? $"Şekil {++n}";
            var color = shape.FillColor ?? shape.StrokeColor ?? "#000000";
            var hint = StitchHint(shape, name, diagnostics);
            var created = hint switch
            {
                StitchType.Satin => Satin(shape, name, ThreadFor(color), diagnostics),
                StitchType.Run => Runs(shape, name, ThreadFor(shape.StrokeColor ?? color)),
                StitchType.Tatami => Tatami(shape, name, ThreadFor(color), diagnostics),
                StitchType.Rope => Ropes(shape, name, ThreadFor(shape.StrokeColor ?? color)),
                _ => Default(shape, name, ThreadFor, autoSatinColumns, diagnostics),
            };
            objects.AddRange(created);
        }

        if (threads.Count == 0) threads.Add(new EmbroideryThread("İplik 1", "#000000"));
        return (threads, objects, diagnostics);
    }

    private static StitchType? StitchHint(ImportedShape shape, string name, List<Diagnostic> diagnostics)
    {
        if (shape.Hints.TryGetValue("satin_column", out var sc) && sc.Equals("true", StringComparison.OrdinalIgnoreCase)) return StitchType.Satin;
        if (!shape.Hints.TryGetValue("stitch", out var value)) return null;
        switch (value.ToLowerInvariant())
        {
            case "run": return StitchType.Run;
            case "satin": return StitchType.Satin;
            case "tatami" or "fill": return StitchType.Tatami;
            case "rope" or "halat": return StitchType.Rope;
            default:
                diagnostics.Add(Diagnostic.Warning("IMP003", $"'{name}': unknown data-stitch=\"{value}\"; the default type was used."));
                return null;
        }
    }

    private static IEnumerable<EmbroideryObject> Default(ImportedShape shape, string name, Func<string, int> threadFor, bool autoSatin, List<Diagnostic> diagnostics)
    {
        var result = new List<EmbroideryObject>();
        if (shape.FillColor is { } fill)
        {
            var fillObjects = Tatami(shape, name, threadFor(fill), diagnostics).ToList();
            if (autoSatin && fillObjects is [TatamiObject tatami] && AutoSatin(tatami, diagnostics, reportRejection: true) is { } columns)
            {
                fillObjects = columns;
            }

            result.AddRange(fillObjects);
        }

        if (shape.StrokeColor is { } stroke)
        {
            var outlineName = shape.FillColor is null ? name : $"{name} kontur";
            result.AddRange(shape.StrokeWidthMm >= SatinStrokeMinMm
                ? Satin(shape with { FillColor = null }, outlineName, threadFor(stroke), diagnostics)
                : Runs(shape, outlineName, threadFor(stroke)));
        }

        return result;
    }

    private static IEnumerable<EmbroideryObject> Tatami(ImportedShape shape, string name, int thread, List<Diagnostic> diagnostics)
    {
        Region region;
        if (shape.FillColor is null)
        {
            // An explicit fill on a stroke fills the area the stroke covers.
            var rings = shape.Subpaths.SelectMany(sp => PolygonOps.BufferPath(ClosedPath(sp), Math.Max(0.5, shape.StrokeWidthMm)).Rings).ToArray();
            region = new Region(rings, FillRule.NonZero);
        }
        else
        {
            region = new Region(shape.Subpaths.Where(s => s.Points.Count >= 3).Select(s => s.Points).ToArray(), shape.FillRule);
        }

        if (region.Rings.Count == 0 || PolygonOps.Area(region) < 0.05)
        {
            diagnostics.Add(Diagnostic.Info("IMP001", $"'{name}' has no fillable area and was skipped."));
            return [];
        }

        return [new TatamiObject { Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, Region = region }];
    }

    /// <summary>Satin columns for a narrow filled shape, or null when a fill suits it better.</summary>
    private static List<EmbroideryObject>? AutoSatin(TatamiObject shape, List<Diagnostic> diagnostics, bool reportRejection)
    {
        var template = new SatinObject { Id = shape.Id, Name = shape.Name, ThreadIndex = shape.ThreadIndex };
        var proposal = AutoColumns.Propose(shape.Region, template);
        if (proposal.Accepted)
        {
            diagnostics.Add(Diagnostic.Info("IMP005", FormattableString.Invariant(
                $"'{shape.Name}': {proposal.Columns.Count} satin column(s) made from the filled outline ({proposal.Coverage:P0} coverage); check the rails.")));
            return [.. proposal.Columns];
        }

        // Only worth mentioning when the shape was narrow enough to be a candidate.
        if (reportRejection && proposal.Columns.Count > 0)
        {
            diagnostics.Add(Diagnostic.Info("IMP004", $"'{shape.Name}' stays a fill: {proposal.Reason}."));
        }

        return null;
    }

    private static IEnumerable<EmbroideryObject> Runs(ImportedShape shape, string name, int thread)
    {
        var paths = shape.Subpaths.Select(ClosedPath).Where(p => p.Length >= 2).ToList();
        return paths.Select((path, i) => (EmbroideryObject)new RunObject
        {
            Id = Guid.NewGuid(),
            Name = paths.Count > 1 ? $"{name} {i + 1}" : name,
            ThreadIndex = thread,
            Path = path,
        });
    }

    private static IEnumerable<EmbroideryObject> Satin(ImportedShape shape, string name, int thread, List<Diagnostic> diagnostics)
    {
        var subpaths = shape.Subpaths.Where(s => s.Points.Count >= 2).ToList();
        if (subpaths.Count == 0) return [];

        if (subpaths.Count >= 2 && shape.FillColor is null)
        {
            // Ink/Stitch convention: the two longest subpaths are rails, the others rungs.
            var ordered = subpaths.OrderByDescending(s => new ArcLengthPath(s.Points).Length).ToList();
            var rungs = ordered.Skip(2).Select(r => new Rung(r.Points[0], r.Points[^1])).ToArray();
            return [new SatinObject
            {
                Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, Source = SatinSource.Rails,
                RailA = ordered[0].Points, RailB = ordered[1].Points, Rungs = rungs,
            }];
        }

        if (shape.FillColor is not null)
        {
            // A filled outline marked as satin: automatic columns when they fit, otherwise two
            // rails split at the outline's extremes.
            var region = new Region(subpaths.Where(s => s.Points.Count >= 3).Select(s => s.Points).ToArray(), shape.FillRule);
            var asTatami = new TatamiObject { Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, Region = region };
            if (region.Rings.Count > 0 && AutoSatin(asTatami, diagnostics, reportRejection: false) is { } columns) return columns;
            var ring = subpaths.MaxBy(s => Math.Abs(PolygonOps.SignedArea(s.Points)))!.Points;
            var (a, b) = ObjectConverter.SplitRing(ring);
            diagnostics.Add(Diagnostic.Info("IMP002", $"'{name}': satin rails were guessed from the outline; check them or add rungs."));
            return [new SatinObject { Id = Guid.NewGuid(), Name = name, ThreadIndex = thread, Source = SatinSource.Rails, RailA = a, RailB = b }];
        }

        var width = Number(shape.Hints, "width") ?? (shape.StrokeWidthMm > 0 ? shape.StrokeWidthMm : 4.0);
        var taper = Number(shape.Hints, "taper") ?? 0;
        return subpaths.Select((sp, i) => (EmbroideryObject)new SatinObject
        {
            Id = Guid.NewGuid(),
            Name = subpaths.Count > 1 ? $"{name} {i + 1}" : name,
            ThreadIndex = thread,
            Source = SatinSource.Stroke,
            Centerline = ClosedPath(sp),
            WidthMm = width,
            StartTaperMm = Number(shape.Hints, "taper-start") ?? taper,
            EndTaperMm = Number(shape.Hints, "taper-end") ?? taper,
        });
    }

    private static IEnumerable<EmbroideryObject> Ropes(ImportedShape shape, string name, int thread)
    {
        var paths = shape.Subpaths.Select(ClosedPath).Where(p => p.Length >= 2).ToList();
        var width = Number(shape.Hints, "width") ?? (shape.StrokeWidthMm >= 1 ? shape.StrokeWidthMm : 4.0);
        var pitch = Number(shape.Hints, "pitch");
        return paths.Select((path, i) => (EmbroideryObject)new RopeObject
        {
            Id = Guid.NewGuid(),
            Name = paths.Count > 1 ? $"{name} {i + 1}" : name,
            ThreadIndex = thread,
            Path = path,
            WidthMm = width,
            Parameters = pitch is { } pt ? new RopeParameters { PitchMm = pt, StrandLengthMm = pt * 2 } : new RopeParameters(),
        });
    }

    private static Vec2[] ClosedPath(FlatSubpath sp) =>
        sp.Closed ? sp.Points.Append(sp.Points[0]).ToArray() : sp.Points.ToArray();

    private static double? Number(IReadOnlyDictionary<string, string> hints, string key) =>
        hints.TryGetValue(key, out var v) && double.TryParse(v.Replace("mm", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0
            ? d
            : null;
}

/// <summary>Converts an object to another stitch type, keeping its geometry as closely as possible.</summary>
public static class ObjectConverter
{
    public const double DefaultSatinWidthMm = 3.0;

    /// <summary>
    /// Like <see cref="Convert"/>, but a fill turned into satin may become several columns
    /// (automatic columns along its skeleton); the first keeps the object's id.
    /// </summary>
    public static IReadOnlyList<EmbroideryObject> ConvertMany(EmbroideryObject item, StitchType target)
    {
        if (item is TatamiObject t && target == StitchType.Satin)
        {
            var template = new SatinObject { Id = item.Id, Name = item.Name, ThreadIndex = item.ThreadIndex, Visible = item.Visible };
            var proposal = AutoColumns.Propose(t.Region, template, template.Parameters.MaxWidthMm);
            if (proposal.Accepted) return proposal.Columns;
        }

        return [Convert(item, target)];
    }

    public static EmbroideryObject Convert(EmbroideryObject item, StitchType target)
    {
        if (item.StitchType == target) return item;
        return (item, target) switch
        {
            (RunObject r, StitchType.Satin) => new SatinObject
            {
                Id = item.Id, Name = item.Name, ThreadIndex = item.ThreadIndex, Visible = item.Visible,
                Source = SatinSource.Stroke, Centerline = r.Path, WidthMm = DefaultSatinWidthMm,
            },
            (RunObject r, StitchType.Tatami) => Tatami(item, RegionFromPath(r.Path)),
            (SatinObject s, StitchType.Run) => Run(item, Centerline(s)),
            (SatinObject s, StitchType.Tatami) => Tatami(item, [Outline(s)]),
            (TatamiObject t, StitchType.Run) => Run(item, OuterRingPath(t.Region)),
            (TatamiObject t, StitchType.Satin) => RailsSatin(item, SplitRing(LargestRing(t.Region))),
            (RopeObject r, StitchType.Run) => Run(item, r.Path),
            (RopeObject r, StitchType.Satin) => new SatinObject
            {
                Id = item.Id, Name = item.Name, ThreadIndex = item.ThreadIndex, Visible = item.Visible,
                Source = SatinSource.Stroke, Centerline = r.Path, WidthMm = r.WidthMm,
            },
            (RopeObject r, StitchType.Tatami) => Tatami(item, PolygonOps.BufferPath(r.Path, r.WidthMm).Rings.ToArray()),
            (_, StitchType.Rope) => Rope(item, item switch
            {
                RunObject r => (r.Path, DefaultSatinWidthMm),
                SatinObject s => (Centerline(s), s.Source == SatinSource.Stroke ? s.WidthMm : DefaultSatinWidthMm),
                TatamiObject t => (OuterRingPath(t.Region), DefaultSatinWidthMm),
                _ => ((IReadOnlyList<Vec2>)[], DefaultSatinWidthMm),
            }),
            _ => throw new InvalidOperationException($"Cannot convert {item.StitchType} to {target}."),
        };
    }

    private static RopeObject Rope(EmbroideryObject from, (IReadOnlyList<Vec2> Path, double Width) g) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, Path = g.Path, WidthMm = Math.Max(1, g.Width) };

    private static RunObject Run(EmbroideryObject from, IReadOnlyList<Vec2> path) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, Path = path };

    private static SatinObject RailsSatin(EmbroideryObject from, (IReadOnlyList<Vec2> A, IReadOnlyList<Vec2> B) rails) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, Source = SatinSource.Rails, RailA = rails.A, RailB = rails.B };

    private static TatamiObject Tatami(EmbroideryObject from, IReadOnlyList<Vec2>[] rings) =>
        new() { Id = from.Id, Name = from.Name, ThreadIndex = from.ThreadIndex, Visible = from.Visible, Region = new Region(rings) };

    /// <summary>A closed outline fills its inside; an open path becomes a band of the default satin width.</summary>
    private static IReadOnlyList<Vec2>[] RegionFromPath(IReadOnlyList<Vec2> path)
    {
        var closed = path.Count > 3 && path[0].ApproximatelyEquals(path[^1], 1e-6);
        if (closed && Math.Abs(PolygonOps.SignedArea(path)) > 0.05) return [path.Take(path.Count - 1).ToArray()];
        return PolygonOps.BufferPath(path, DefaultSatinWidthMm).Rings.ToArray();
    }

    private static IReadOnlyList<Vec2> Centerline(SatinObject s)
    {
        if (s.Source == SatinSource.Stroke) return s.Centerline;
        if (SatinLadder.Build(s).Value is not { } ladder) return [];
        var n = Math.Max(2, (int)Math.Ceiling(ladder.Length / 0.5));
        return Enumerable.Range(0, n + 1).Select(i =>
        {
            var (a, b) = ladder.At(ladder.Length * i / n);
            return Vec2.Lerp(a, b, 0.5);
        }).ToArray();
    }

    /// <summary>The column's outline: rail A forward, rail B back.</summary>
    private static IReadOnlyList<Vec2> Outline(SatinObject s)
    {
        if (SatinLadder.Build(s).Value is not { } ladder) return [];
        return ladder.A.Concat(ladder.B.Reverse()).ToArray();
    }

    private static IReadOnlyList<Vec2> LargestRing(Region region) =>
        region.Rings.MaxBy(r => Math.Abs(PolygonOps.SignedArea(r))) ?? [];

    private static IReadOnlyList<Vec2> OuterRingPath(Region region)
    {
        var ring = LargestRing(region);
        return ring.Count == 0 ? [] : ring.Append(ring[0]).ToArray();
    }

    /// <summary>
    /// Splits a closed outline into two rails at its two most distant vertices. Good for
    /// elongated shapes; users refine the rails afterwards.
    /// </summary>
    public static (IReadOnlyList<Vec2> A, IReadOnlyList<Vec2> B) SplitRing(IReadOnlyList<Vec2> ring)
    {
        if (ring.Count < 3) return (ring, ring);
        var step = Math.Max(1, ring.Count / 400);
        int bi = 0, bj = 0;
        double best = -1;
        for (var i = 0; i < ring.Count; i += step)
        {
            for (var j = i + 1; j < ring.Count; j += step)
            {
                var d = (ring[i] - ring[j]).LengthSquared;
                if (d > best) (best, bi, bj) = (d, i, j);
            }
        }

        var a = new List<Vec2>();
        for (var k = bi; ; k = (k + 1) % ring.Count)
        {
            a.Add(ring[k]);
            if (k == bj) break;
        }

        var b = new List<Vec2>();
        for (var k = bi; ; k = (k - 1 + ring.Count) % ring.Count)
        {
            b.Add(ring[k]);
            if (k == bj) break;
        }

        return (a, b);
    }
}
