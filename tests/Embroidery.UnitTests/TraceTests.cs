using Embroidery.Application.Analysis;
using Embroidery.Application.Export;
using Embroidery.Application.Projects;
using Embroidery.Core.Objects;
using Embroidery.Formats.Dst;
using Embroidery.StitchEngine.Generators;

namespace Embroidery.UnitTests;

public class TraceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Artwork = """
        <svg xmlns="http://www.w3.org/2000/svg" width="90mm" height="50mm" viewBox="0 0 90 50">
          <path id="dar" d="M5,10 L40,10" stroke="#d4a53c" stroke-width="2.5" fill="none" data-stitch="satin"/>
          <path id="kivrim" d="M5,25 C20,15 30,40 45,25" stroke="#d4a53c" stroke-width="4" fill="none" data-stitch="satin"/>
          <path id="genis" d="M55,5 L55,45" stroke="#1f4e9c" stroke-width="6" fill="none" data-stitch="satin"/>
          <path id="cizgi" d="M65,40 L85,10" stroke="#1f4e9c" stroke-width="0.3" fill="none"/>
        </svg>
        """;

    private static (ProjectService Service, byte[] Dst) Sewn()
    {
        var service = new ProjectService();
        var design = service.ImportSvg("kaynak.svg", Artwork).Design;
        return (service, service.ExportDst(design.Id).Data);
    }

    [Fact]
    public void Satin_columns_and_runs_come_back_from_a_dst()
    {
        var (_, dst) = Sewn();
        var result = DstTracer.Trace(DstReader.Read(dst).Plan, "iz", new TraceOptions { PullCompensationMm = 0.2 });
        foreach (var d in result.Diagnostics) output.WriteLine($"{d.Code} {d.Message}");

        Assert.Equal(3, result.SatinColumns);
        Assert.Equal(1, result.RunObjects);
        Assert.Equal(2, result.Design.Threads.Count);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "TRC002");

        var widths = result.Design.Objects.OfType<SatinObject>()
            .Select(s => SatinLadder.Build(s).Value!).Select(l => l.A.Zip(l.B, (a, b) => (a - b).Length).OrderBy(x => x).ElementAt(l.A.Length / 2))
            .OrderBy(x => x).ToList();
        output.WriteLine(string.Join(", ", widths.Select(w => w.ToString("0.00"))));
        Assert.InRange(widths[0], 2.2, 2.8);
        Assert.InRange(widths[1], 3.7, 4.3);
        Assert.InRange(widths[2], 5.7, 6.3);
    }

    [Fact]
    public void Traced_svg_imports_and_sews_like_the_original()
    {
        var (service, dst) = Sewn();
        var original = DstReader.Read(dst).Plan;
        var traced = DstTracer.Trace(original, "iz", new TraceOptions { PullCompensationMm = 0.2 });
        var svg = DesignSvgWriter.Write(traced.Design);

        var reimported = service.ImportSvg("iz.svg", svg).Design;
        Assert.Equal(traced.Design.Objects.Select(o => o.StitchType), reimported.Objects.Select(o => o.StitchType));
        var rails = reimported.Objects.OfType<SatinObject>().ToList();
        Assert.All(rails, s => Assert.Equal(SatinSource.Rails, s.Source));

        var again = StitchMetrics.From(DstReader.Read(service.ExportDst(reimported.Id).Data).Plan);
        var reference = StitchMetrics.From(original);
        output.WriteLine($"stitches {reference.Stitches} -> {again.Stitches}, throw {reference.SatinThrowMm!.P50:0.00} -> {again.SatinThrowMm!.P50:0.00}");
        Assert.InRange(again.Stitches, reference.Stitches * 0.85, reference.Stitches * 1.15);
        Assert.InRange(again.SatinThrowMm!.P50, reference.SatinThrowMm!.P50 - 0.3, reference.SatinThrowMm.P50 + 0.3);
        Assert.InRange(again.WidthMm, reference.WidthMm - 1, reference.WidthMm + 1);
    }

    [Fact]
    public void Service_opens_a_dst_as_an_editable_project_and_exports_svg()
    {
        var (service, dst) = Sewn();
        var imported = service.ImportDst("musteri.dst", dst, new TraceOptions { PullCompensationMm = 0.2 });
        Assert.Equal("musteri", imported.Design.Name);
        Assert.Equal(4, imported.Design.Objects.Count);
        Assert.Contains(imported.Diagnostics, d => d.Code == "TRC001");

        var svg = System.Text.Encoding.UTF8.GetString(service.ExportSvg(imported.Design.Id).Data);
        Assert.Contains("data-stitch=\"satin\"", svg);
        Assert.Equal(4, service.ImportSvg("geri.svg", svg).Design.Objects.Count);

        Assert.Throws<DesignValidationException>(() => service.ImportDst("bozuk.dst", [1, 2, 3]));
    }
}
