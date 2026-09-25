using System.Text.Json;
using Embroidery.Application.Caching;
using Embroidery.Application.Import;
using Embroidery.Application.Projects;
using Embroidery.Application.Serialization;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Formats.Dst;
using Embroidery.Machine;
using Embroidery.StitchEngine;

namespace Embroidery.UnitTests;

public class ProjectServiceTests
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="60mm" height="40mm" viewBox="0 0 60 40">
          <rect id="box" x="5" y="5" width="30" height="20" fill="#336699"/>
          <path id="line" d="M5,32 L55,32" stroke="#cc0000" stroke-width="0.3" fill="none"/>
          <path id="bar" d="M40,5 L40,30" stroke="#00aa00" stroke-width="3" fill="none"/>
        </svg>
        """;

    private static (ProjectService Service, Design Design) Import()
    {
        var service = new ProjectService();
        var result = service.ImportSvg("logo.svg", Svg);
        return (service, result.Design);
    }

    [Fact]
    public void Import_creates_objects_by_shape_kind()
    {
        var (_, design) = Import();
        Assert.Equal("logo", design.Name);
        Assert.Collection(design.Objects,
            o => Assert.IsType<TatamiObject>(o),
            o => Assert.IsType<RunObject>(o),
            o => Assert.IsType<SatinObject>(o));
        Assert.Equal(3, design.Threads.Count);
        Assert.Equal("#336699", design.Threads[0].ColorHex);
    }

    [Fact]
    public void Edits_bump_revision_and_stale_edits_conflict()
    {
        var (service, design) = Import();
        var tatami = (TatamiObject)design.Objects[0];
        var edited = service.UpdateObject(design.Id, design.Revision, tatami with { Parameters = tatami.Parameters with { AngleDeg = 0 } });
        Assert.Equal(design.Revision + 1, edited.Revision);

        Assert.Throws<RevisionConflictException>(() =>
            service.UpdateObject(design.Id, design.Revision, tatami));
    }

    [Fact]
    public void Invalid_parameters_are_rejected_without_changing_the_design()
    {
        var (service, design) = Import();
        var run = (RunObject)design.Objects[1];
        Assert.Throws<DesignValidationException>(() =>
            service.UpdateObject(design.Id, design.Revision, run with { Parameters = run.Parameters with { StitchLengthMm = 0 } }));
        Assert.Equal(design.Revision, service.Get(design.Id).Revision);
    }

    [Fact]
    public void Undo_and_redo_restore_designs_with_new_revisions()
    {
        var (service, design) = Import();
        var afterDelete = service.DeleteObject(design.Id, design.Revision, design.Objects[1].Id);
        Assert.Equal(2, afterDelete.Objects.Count);

        var undone = service.Undo(design.Id);
        Assert.Equal(3, undone.Objects.Count);
        Assert.True(undone.Revision > afterDelete.Revision);
        Assert.True(service.CanRedo(design.Id));

        var redone = service.Redo(design.Id);
        Assert.Equal(2, redone.Objects.Count);
        Assert.False(service.CanRedo(design.Id));
    }

    [Fact]
    public void Unchanged_objects_are_served_from_cache()
    {
        var cache = new GenerationCache();
        var service = new ProjectService(cache);
        var design = service.ImportSvg("logo.svg", Svg).Design;

        service.Preview(design.Id);
        var missesAfterFirst = cache.Misses;
        Assert.Equal(3, missesAfterFirst);

        service.Preview(design.Id);
        Assert.Equal(missesAfterFirst, cache.Misses);

        // Renaming does not invalidate; changing a parameter invalidates only that object.
        var run = (RunObject)design.Objects[1];
        var d2 = service.UpdateObject(design.Id, null, run with { Name = "renamed" });
        service.Preview(design.Id);
        Assert.Equal(missesAfterFirst, cache.Misses);

        service.UpdateObject(design.Id, d2.Revision, run with { Parameters = run.Parameters with { StitchLengthMm = 2 } });
        service.Preview(design.Id);
        Assert.Equal(missesAfterFirst + 1, cache.Misses);
    }

    [Fact]
    public void Content_hash_is_stable_and_ignores_name()
    {
        var obj = new RunObject { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "a", ThreadIndex = 0, Path = [new(0, 0), new(1, 2.5)] };
        var k1 = ObjectGenerationKey.For(obj, GenerationContext.Default);
        var k2 = ObjectGenerationKey.For(obj with { Name = "b" }, GenerationContext.Default);
        Assert.Equal(k1, k2);
        Assert.Equal(64, k1.ContentHash.Length);
        Assert.NotEqual(k1, ObjectGenerationKey.For(obj with { Path = [new(0, 0), new(1, 2.6)] }, GenerationContext.Default));
    }

    [Fact]
    public void Embx_round_trip_preserves_the_design()
    {
        var (service, design) = Import();
        var package = service.ExportEmbx(design.Id).Data;
        var reopened = service.Open(new MemoryStream(package));

        Assert.NotEqual(design.Id, reopened.Id); // the original is still open
        string Json(Design d) => JsonSerializer.Serialize(d with { Id = Guid.Empty, Revision = 0 }, EmbroideryJson.Options);
        Assert.Equal(Json(design), Json(reopened));
        Assert.Equal(Svg, reopened.Artwork!.SvgText);
    }

    [Fact]
    public void Dst_export_reads_back_with_expected_color_changes()
    {
        var (service, design) = Import();
        var export = service.ExportDst(design.Id);
        Assert.EndsWith(".dst", export.FileName);

        var (header, plan) = DstReader.Read(export.Data);
        Assert.Equal(2, header.ColorChanges); // three threads
        Assert.Equal(EncodedCommand.End, plan.Stitches[^1].Command);
        Assert.True(plan.StitchCount > 100);
    }

    [Fact]
    public void Reorder_requires_a_permutation()
    {
        var (service, design) = Import();
        var ids = design.Objects.Select(o => o.Id).Reverse().ToList();
        var reordered = service.Reorder(design.Id, design.Revision, ids);
        Assert.Equal(ids, reordered.Objects.Select(o => o.Id));
        Assert.Throws<DesignValidationException>(() => service.Reorder(design.Id, null, ids.Take(2).ToList()));
    }

    [Theory]
    [InlineData(StitchType.Run)]
    [InlineData(StitchType.Satin)]
    [InlineData(StitchType.Tatami)]
    [InlineData(StitchType.Rope)]
    public void Every_object_converts_to_every_type_and_still_generates(StitchType target)
    {
        var (service, design) = Import();
        foreach (var obj in design.Objects)
        {
            var converted = ObjectConverter.Convert(obj, target);
            Assert.Equal(target, converted.StitchType);
            Assert.Equal(obj.Id, converted.Id);
            var result = new ObjectGenerator().Generate(converted, GenerationContext.Default);
            Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.NotEmpty(result.Value.Stitches);
        }

        Assert.NotNull(service);
    }
}
