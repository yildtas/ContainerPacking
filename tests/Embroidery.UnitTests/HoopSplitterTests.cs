using System.IO.Compression;
using Embroidery.Application.Analysis;
using Embroidery.Application.Projects;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Core.Primitives;
using Embroidery.Formats.Dst;

namespace Embroidery.UnitTests;

public class HoopSplitterTests
{
    /// <summary>A 280 × 480 mm panel of small satin motifs, like FER-7.</summary>
    private static Design Panel()
    {
        var objects = new List<EmbroideryObject>();
        for (var x = 10.0; x < 280; x += 30)
        {
            for (var y = 10.0; y < 480; y += 30)
            {
                objects.Add(new SatinObject
                {
                    Id = Guid.NewGuid(), Name = $"m{objects.Count}", ThreadIndex = 0, Source = SatinSource.Stroke,
                    Centerline = [new(x, y), new(x + 15, y + 10)], WidthMm = 4,
                });
            }
        }

        return new Design
        {
            Id = Guid.NewGuid(), Name = "panel", Threads = [new EmbroideryThread("altın", "#D4A53C")],
            Objects = objects, Hoop = new Hoop("200x200", 200, 200),
        };
    }

    [Fact]
    public void Large_design_splits_into_parts_that_fit_and_cover_every_object_once()
    {
        var design = Panel();
        var result = HoopSplitter.Split(design, design.Hoop);
        Assert.True(result.Succeeded);
        Assert.True(result.Parts.Count >= 6, $"{result.Parts.Count} parts");
        Assert.All(result.Parts, p => Assert.True(p.Area.Width <= 200 && p.Area.Height <= 200, $"part {p.Number}: {p.Area.Width} × {p.Area.Height}"));

        var assigned = result.Parts.SelectMany(p => p.Design.Objects).Where(o => !o.Name.StartsWith(HoopSplitter.AlignmentThreadName)).Select(o => o.Id).ToList();
        Assert.Equal(design.Objects.Count, assigned.Count);
        Assert.Equal(design.Objects.Select(o => o.Id).OrderBy(x => x), assigned.OrderBy(x => x));
    }

    [Fact]
    public void Neighbouring_parts_share_registration_marks_sewn_first_with_their_own_thread()
    {
        var result = HoopSplitter.Split(Panel(), new Hoop("200x200", 200, 200));
        static IEnumerable<Vec2> Marks(HoopPart p) =>
            p.Design.Objects.OfType<RunObject>().Where(o => o.Name.StartsWith(HoopSplitter.AlignmentThreadName)).Select(o => o.Path[2]);

        var first = result.Parts[0];
        Assert.StartsWith(HoopSplitter.AlignmentThreadName, first.Design.Objects[0].Name);
        Assert.Equal(HoopSplitter.AlignmentThreadName, first.Design.Threads[first.Design.Objects[0].ThreadIndex].Name);

        var neighbour = result.Parts.First(p => p != first && Math.Abs(p.Column - first.Column) + Math.Abs(p.Row - first.Row) == 1);
        var shared = Marks(first).Where(m => Marks(neighbour).Any(n => n.ApproximatelyEquals(m, 1e-9))).ToList();
        Assert.Equal(2, shared.Count);
    }

    [Fact]
    public void Designs_that_fit_are_not_split_and_oversized_objects_are_reported()
    {
        var small = Panel() with { Objects = Panel().Objects.Take(3).ToList() };
        Assert.Single(HoopSplitter.Split(small, new Hoop("200x200", 200, 200)).Parts);

        var huge = small with
        {
            Objects = [new RunObject { Id = Guid.NewGuid(), Name = "long", ThreadIndex = 0, Path = [new(0, 0), new(250, 250)] }],
        };
        var result = HoopSplitter.Split(huge, new Hoop("200x200", 200, 200));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "HOOP001");
    }

    [Fact]
    public void Service_exports_one_dst_per_hooping_with_a_guide()
    {
        var service = new ProjectService();
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"280mm\" height=\"100mm\" viewBox=\"0 0 280 100\">"
            + string.Concat(Enumerable.Range(0, 9).Select(i => $"<path d=\"M{10 + i * 30},50 L{25 + i * 30},60\" stroke=\"#d4a53c\" stroke-width=\"4\" fill=\"none\"/>"))
            + "</svg>";
        var design = service.ImportSvg("wide.svg", svg).Design;
        service.UpdateSettings(design.Id, null, new Hoop("130x180", 130, 180), null, null);

        using var zip = new ZipArchive(new MemoryStream(service.ExportDstParts(design.Id).Data));
        var dsts = zip.Entries.Where(e => e.Name.EndsWith(".dst")).ToList();
        Assert.True(dsts.Count >= 2);
        Assert.Contains(zip.Entries, e => e.Name == "KASNAKLAMA.txt");
        foreach (var e in dsts)
        {
            using var s = e.Open();
            var (_, plan) = DstReader.Read(s);
            var m = StitchMetrics.From(plan);
            Assert.True(m.WidthMm <= 180 && m.HeightMm <= 180, $"{e.Name}: {m.WidthMm} × {m.HeightMm}");
            Assert.True(m.ColorChanges >= 1); // alignment marks first, then the design thread
        }
    }
}
